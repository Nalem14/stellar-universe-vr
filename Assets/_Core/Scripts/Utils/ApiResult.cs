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
    }
}
