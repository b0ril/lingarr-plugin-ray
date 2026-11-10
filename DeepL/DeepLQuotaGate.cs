namespace Lingarr.Plugin.Ray.DeepL;

/// <summary>
/// Process-lifetime sticky skip for DeepL after quota exhaustion.
/// Cleared when a usage check reports room remaining (or via <see cref="Clear"/>).
/// </summary>
public sealed class DeepLQuotaGate
{
    private int _exhausted;

    public bool IsExhausted => Volatile.Read(ref _exhausted) != 0;

    public void MarkExhausted() => Volatile.Write(ref _exhausted, 1);

    public void Clear() => Volatile.Write(ref _exhausted, 0);

    public void RefreshFromUsage(bool anyLimitReached)
    {
        if (anyLimitReached)
        {
            MarkExhausted();
        }
        else
        {
            Clear();
        }
    }
}
