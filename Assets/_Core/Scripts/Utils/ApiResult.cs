namespace Core.Utils
{
    public readonly struct ApiResult
    {
        public bool Ok { get; }
        public string Body { get; }
        public string Error { get; }

        ApiResult(bool ok, string body, string error)
        {
            Ok = ok;
            Body = body ?? string.Empty;
            Error = error;
        }

        public static ApiResult Success(string body) => new(true, body, null);

        public static ApiResult Fail(string error) => new(false, string.Empty, error ?? "unknown");

        /// <summary>
        /// Success that carries a server notice (MoveFleet* fell back to sublight). Returns the native
        /// GetTranslations key the web toasts for it, or null. Same mapping as web objects/fleet.js.
        /// </summary>
        public string NoticeKey
        {
            get
            {
                if (!Ok || !Body.StartsWith("ok:", System.StringComparison.Ordinal))
                    return null;
                if (Body.StartsWith("ok:sublight_no_crystal", System.StringComparison.Ordinal))
                    return "notEnoughCrystalForHyperspace";
                if (Body.StartsWith("ok:sublight_not_enough_modules", System.StringComparison.Ordinal))
                    return "notEnoughHyperspaceModules";
                // Not enough crystal in the hold for a faster sub-light trip: speed 1, free (web f03197d).
                if (Body.StartsWith("ok:conventional_drive", System.StringComparison.Ordinal))
                    return "conventionalDrive";
                // The Fuel Tanks paid the trip (model/fuel.php): less of it than crystal, and faster.
                if (Body.StartsWith("ok:fuel_drive", System.StringComparison.Ordinal))
                    return "fuelDrive";
                return null;
            }
        }
    }
}
