//using POSAPP.Reports;
//using System;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Text.Json;

//public static class D365SyncWorker
//{
//    private static readonly DynamicsApiClient _api = new();
//    private const string DataAreaId = "1110";

//    // ══════════════════════════════════════════════════════════════════════════
//    //  Entry point — call this on a timer every 3 minutes
//    // ══════════════════════════════════════════════════════════════════════════

//    public static async Task ProcessPendingAsync(int maxRetry = 5)
//    {
//        try
//        {
//            SalesRepository.EnsurePosApiResponseTable();
//            System.Diagnostics.Debug.WriteLine("[Sync] PosApiResponse table ensured.");
//        }
//        catch (Exception ex)
//        {
//            System.Diagnostics.Debug.WriteLine($"[Sync] Table creation failed: {ex.Message}");
//            return;
//        }

//        // Reset any rows stuck in 'Processing' (e.g. app crashed mid-sync)
//        SalesRepository.ResetStuckProcessingRows(stuckMinutes: 10);

//        var pending = SalesRepository.GetPendingSyncQueue(maxRetry);
//        System.Diagnostics.Debug.WriteLine($"[Sync] Found {pending.Count} pending rows.");

//        foreach (var row in pending)
//            await PushOneAsync(row);
//    }

//    // ══════════════════════════════════════════════════════════════════════════
//    //  Process a single queue row
//    // ══════════════════════════════════════════════════════════════════════════
//    private static async Task PushOneAsync(SyncQueueRow row)
//    {
//        System.Diagnostics.Debug.WriteLine(
//            $"[Sync] Processing QueueId={row.QueueId} InvoiceNo={row.InvoiceNo} Retry={row.RetryCount}");

//        RetailSalesHeader header;
//        List<RetailSalesLine> lines;

//        try
//        {
//            header = SalesRepository.BuildD365Header(row.TransactionId, DataAreaId);
//            lines = SalesRepository.BuildD365Lines(row.TransactionId);
//        }
//        catch (Exception ex)
//        {
//            SalesRepository.CompleteSyncAttempt(row.QueueId, 0, false, null,
//                $"Payload build error: {ex.Message}");
//            return;
//        }

//        string requestJson = JsonSerializer.Serialize(new { Header = header, Lines = lines });
//        long logId = SalesRepository.BeginSyncAttempt(row.QueueId, row.TransactionId, requestJson);

//        string companyId = header.CompanyId ?? "";
//        string storeId = header.StoreId ?? "";
//        string invoiceId = header.BISInvoiceId ?? "";

//        const string headerEndpoint =
//            "https://ridevfccab5234b1f351cdevaos.axcloud.dynamics.com/data/RetailSalesHeaders?cross-company=true";
//        const string lineEndpoint =
//            "https://ridevfccab5234b1f351cdevaos.axcloud.dynamics.com/data/RetailSalesLines?cross-company=true";

//        try
//        {
//            // ── Bug 2 fix: check if header was already successfully sent ──────────
//            bool headerAlreadySynced = SalesRepository.HasSuccessfulApiResponse(invoiceId, "Header");

//            if (!headerAlreadySynced)
//            {
//                string headerRequest = JsonSerializer.Serialize(header);
//                var (headerResponse, headerStatus) = await _api.PostRawAsync(headerEndpoint, header);

//                System.Diagnostics.Debug.WriteLine(
//                    $"[Sync] Header POST status={headerStatus} invoice={invoiceId}");

//                SalesRepository.InsertPosApiResponse(companyId, storeId, "Header", invoiceId,
//                    headerRequest, headerResponse, headerStatus.ToString(),
//                    headerStatus >= 200 && headerStatus < 300 ? "Success" : "Failed");

//                if (headerStatus < 200 || headerStatus >= 300)
//                    throw new Exception($"Header POST failed ({headerStatus}): {headerResponse}");
//            }
//            else
//            {
//                System.Diagnostics.Debug.WriteLine(
//                    $"[Sync] Header already synced for {invoiceId} — skipping re-POST");
//            }

//            // ── Lines: only send lines not yet successfully posted ────────────────
//            for (int i = 0; i < lines.Count; i++)
//            {
//                string lineType = $"Line_{i + 1}";
//                bool lineAlreadySynced = SalesRepository.HasSuccessfulApiResponse(invoiceId, lineType);

//                if (lineAlreadySynced)
//                {
//                    System.Diagnostics.Debug.WriteLine(
//                        $"[Sync] {lineType} already synced for {invoiceId} — skipping");
//                    continue;
//                }

//                var line = lines[i];
//                line.dataAreaId = DataAreaId;
//                line.BISInvoiceId = invoiceId;
//                line.InvoiceLineId = i + 1;
//                line.InvoiceId = null;

//                string lineRequest = JsonSerializer.Serialize(line);
//                var (lineResponse, lineStatus) = await _api.PostRawAsync(lineEndpoint, line);

//                System.Diagnostics.Debug.WriteLine(
//                    $"[Sync] {lineType} POST status={lineStatus} invoice={invoiceId}");

//                SalesRepository.InsertPosApiResponse(companyId, storeId, lineType, invoiceId,
//                    lineRequest, lineResponse, lineStatus.ToString(),
//                    lineStatus >= 200 && lineStatus < 300 ? "Success" : "Failed");

//                if (lineStatus < 200 || lineStatus >= 300)
//                    throw new Exception($"{lineType} POST failed ({lineStatus}): {lineResponse}");
//            }

//            // ── Poll for D365 confirmation ────────────────────────────────────────
//            string? d365SalesOrderId = null;
//            string? d365InvoiceId = null;

//            for (int attempt = 0; attempt < 5; attempt++)
//            {
//                await Task.Delay(2000);
//                var responses = await _api.GetRetailSalesInvoicePaymentResponsesAsync();
//                var match = responses.FirstOrDefault(p => p.BISInvoiceId == invoiceId);

//                if (match != null)
//                {
//                    d365SalesOrderId = match.SalesId;
//                    d365InvoiceId = match.InvoiceId;
//                    System.Diagnostics.Debug.WriteLine(
//                        $"[Sync] Poll matched: SalesId={d365SalesOrderId} InvoiceId={d365InvoiceId}");
//                    SalesRepository.InsertPosApiResponse(companyId, storeId, "PollResponse", invoiceId,
//                        $"GET PaymentResponses filter BISInvoiceId={invoiceId}",
//                        JsonSerializer.Serialize(match), "200", "Success");
//                    break;
//                }
//                System.Diagnostics.Debug.WriteLine(
//                    $"[Sync] Poll attempt {attempt + 1}/5 — no match yet for {invoiceId}");
//            }

//            SalesRepository.CompleteSyncAttempt(row.QueueId, logId, true,
//                JsonSerializer.Serialize(new { D365SalesOrderId = d365SalesOrderId, D365InvoiceId = d365InvoiceId }),
//                "Synced successfully", d365SalesOrderId, d365InvoiceId);

//            System.Diagnostics.Debug.WriteLine($"[Sync] QueueId={row.QueueId} marked Synced.");
//        }
//        catch (Exception ex)
//        {
//            System.Diagnostics.Debug.WriteLine($"[Sync] QueueId={row.QueueId} FAILED: {ex.Message}");
//            SalesRepository.InsertPosApiResponse(companyId, storeId, "Error", invoiceId,
//                requestJson, ex.Message, "0", "Failed");
//            SalesRepository.CompleteSyncAttempt(row.QueueId, logId, false, null, ex.Message);
//        }
//    }
//}