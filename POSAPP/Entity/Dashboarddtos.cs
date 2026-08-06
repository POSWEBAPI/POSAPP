namespace POSAPP.Entity
{
    /// <summary>
    /// Represents one row in the Top Selling Products widget.
    /// Populated from SOInvoiceLine aggregated by ItemID.
    /// </summary>
    public class TopSellingProductDto
    {
        /// <summary>Item display name (from SOInvoiceLine.ItemName or joined Item table).</summary>
        public string ItemName { get; set; } = "";

        /// <summary>Total units sold across all invoices in the selected period.</summary>
        public int TotalSold { get; set; }

        /// <summary>Formatted selling price string, e.g. "P 123.00".</summary>
        public string PriceFormatted { get; set; } = "";

        /// <summary>Raw unit price for bar-width calculations.</summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// Percentage of this item's sales vs the top seller (0–100).
        /// Used to drive the progress bar width in the chart.
        /// </summary>
        public int BarPercent { get; set; }
    }

    /// <summary>
    /// Represents one row in the Low Stock Alerts widget.
    /// Populated from StockMovement net-quantity calculation.
    /// </summary>
    public class LowStockAlertDto
    {
        /// <summary>Item display name.</summary>
        public string ItemName { get; set; } = "";

        /// <summary>Current net stock quantity (inbound - outbound movements).</summary>
        public decimal CurrentQty { get; set; }

        /// <summary>"Low" or "Critical" — determined by threshold logic in the repository.</summary>
        public string Status { get; set; } = "Low";

        /// <summary>Item code / SKU for reference.</summary>
        public string ItemCode { get; set; } = "";
    }
}