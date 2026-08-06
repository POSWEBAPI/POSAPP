using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace D365SyncService
{
    [RunInstaller(true)]
    public class SyncServiceInstaller : Installer   // renamed to avoid clash with System.ServiceProcess.ServiceInstaller
    {
        public SyncServiceInstaller()
        {
            var processInstaller = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem
            };

            var serviceInstaller = new System.ServiceProcess.ServiceInstaller
            {
                ServiceName = "D365POSSyncService",
                DisplayName = "D365 POS Product Sync Service",
                Description = "Syncs product data from Dynamics 365 API into " +
                              "ShriPOS.db every 5 minutes. Used by POSAPP.",
                StartType = ServiceStartMode.Automatic
            };

            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }
    }
}   