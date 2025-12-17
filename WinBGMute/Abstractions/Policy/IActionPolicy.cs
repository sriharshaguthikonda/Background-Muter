using WinBGMuter.Abstractions.Audio;

namespace WinBGMuter.Abstractions.Policy
{
    public interface IActionPolicy
    {
        ActionMode Mode { get; }

        bool ShouldAffectProcess(AudioProcessState process);

        bool ShouldResumeProcess(AudioProcessState process);
    }
}
