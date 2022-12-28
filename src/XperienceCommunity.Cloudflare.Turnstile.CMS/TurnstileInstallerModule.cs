using CMS.Base;
using CMS.Core;
using CMS.DataEngine;

namespace XperienceCommunity.Cloudflare.Turnstile.CMS
{
    public class TurnstileInstallerModule : Module
    {
        public TurnstileInstallerModule() : base(nameof(TurnstileInstallerModule)) { }

        protected override void OnPreInit()
        {
            base.OnPreInit();

            Service.Use<ITurnstileSettingsInstaller, TurnstileSettingsInstaller>();
        }

        protected override void OnInit()
        {
            if (IsRunningInCmsApp())
            {
                var installer = Service.Resolve<ITurnstileSettingsInstaller>();

                installer.Install();
            }

            base.OnInit();
        }

        private static bool IsRunningInCmsApp() => SystemContext.IsCMSRunningAsMainApplication && SystemContext.IsWebSite;
    }
}
