namespace WinBGMuter.Abstractions.Media
{
    public enum MediaCommandFailureReason
    {
        None = 0,
        NotSupported,
        NotFound,
        Ambiguous,
        UnknownError
    }
}
