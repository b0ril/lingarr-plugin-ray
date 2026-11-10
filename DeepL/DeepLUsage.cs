namespace Lingarr.Plugin.Ray.DeepL;

public readonly record struct DeepLUsage(long CharacterCount, long CharacterLimit)
{
    public bool AnyLimitReached => CharacterLimit > 0 && CharacterCount >= CharacterLimit;
}
