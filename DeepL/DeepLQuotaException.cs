namespace Lingarr.Plugin.Ray.DeepL;

public sealed class DeepLQuotaException : Exception
{
    public DeepLQuotaException(string message) : base(message)
    {
    }

    public DeepLQuotaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
