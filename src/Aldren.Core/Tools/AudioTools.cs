using Aldren.Inference;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Aldren.Windows.Audio;

namespace Aldren.Core.Tools;

public sealed record VolumeResult(int Volume, bool Muted);

public sealed record SetVolumeArgs(int Level);

public sealed class SetVolumeTool : Tool<SetVolumeArgs, VolumeResult>
{
    public override string Name => "set_volume";
    public override string Description => "Set the system volume to a level from 0 to 100";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.AudioVolume;

    public override IEnumerable<string> Validate(SetVolumeArgs args)
    {
        if (args.Level is < 0 or > 100)
        {
            yield return "level must be 0–100.";
        }
    }

    public override string Summarize(SetVolumeArgs args) => $"Set the volume to {args.Level}%";

    protected override Task<VolumeResult> ExecuteAsync(SetVolumeArgs args, CancellationToken ct)
    {
        SystemAudio.SetVolume(args.Level);
        return Task.FromResult(new VolumeResult(SystemAudio.GetVolume(), SystemAudio.GetMute()));
    }

    protected override Task<bool> VerifyAsync(SetVolumeArgs args, VolumeResult result, CancellationToken ct) =>
        Task.FromResult(Math.Abs(result.Volume - args.Level) <= 1);
}

public sealed record ChangeVolumeArgs(int Delta);

public sealed class ChangeVolumeTool : Tool<ChangeVolumeArgs, VolumeResult>
{
    public override string Name => "change_volume";
    public override string Description => "Raise or lower the system volume by a number of points (-100 to 100)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.AudioVolume;

    public override IEnumerable<string> Validate(ChangeVolumeArgs args)
    {
        if (args.Delta is < -100 or > 100 or 0)
        {
            yield return "delta must be -100–100 and not 0.";
        }
    }

    protected override Task<VolumeResult> ExecuteAsync(ChangeVolumeArgs args, CancellationToken ct)
    {
        SystemAudio.SetVolume(Math.Clamp(SystemAudio.GetVolume() + args.Delta, 0, 100));
        return Task.FromResult(new VolumeResult(SystemAudio.GetVolume(), SystemAudio.GetMute()));
    }
}

public sealed record MuteArgs(bool Mute);

public sealed class MuteTool : Tool<MuteArgs, VolumeResult>
{
    public override string Name => "mute";
    public override string Description => "Mute (true) or unmute (false) the system sound";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.AudioVolume;

    protected override Task<VolumeResult> ExecuteAsync(MuteArgs args, CancellationToken ct)
    {
        SystemAudio.SetMute(args.Mute);
        return Task.FromResult(new VolumeResult(SystemAudio.GetVolume(), SystemAudio.GetMute()));
    }

    protected override Task<bool> VerifyAsync(MuteArgs args, VolumeResult result, CancellationToken ct) =>
        Task.FromResult(result.Muted == args.Mute);
}

public sealed record MediaArgs(string Action);

public sealed class MediaControlTool(ResidencyManager residency) : Tool<MediaArgs, string>
{
    private static readonly Dictionary<string, MediaAction> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["play_pause"] = MediaAction.PlayPause,
        ["next"] = MediaAction.Next,
        ["previous"] = MediaAction.Previous,
        ["stop"] = MediaAction.Stop,
    };

    public override string Name => "media_control";
    public override string Description => "Control media playback: action is play_pause, next, previous or stop";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.MediaControl;

    public override IEnumerable<string> Validate(MediaArgs args)
    {
        if (!Actions.ContainsKey(args.Action ?? ""))
        {
            yield return "action must be play_pause, next, previous or stop.";
        }
    }

    protected override Task<string> ExecuteAsync(MediaArgs args, CancellationToken ct)
    {
        if (residency.GamingMode)
        {
            throw new InvalidOperationException("Gaming mode is on, so I don't send media commands.");
        }

        return MediaCommands.Send(Actions[args.Action])
            ? Task.FromResult(args.Action)
            : throw new InvalidOperationException("The Windows shell isn't available.");
    }
}
