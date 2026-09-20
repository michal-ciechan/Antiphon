namespace Antiphon.Card0490.NativeHarness;

public sealed class CustodyPipeHandoff
{
    public bool TransferAllowed { get; private set; } = true;
    public bool ExitRootSent { get; private set; }
    public int OpenOriginalEndsAtReleased { get; private set; }
    public bool OriginalHandlesOpenUntilAdopted { get; private set; } = true;
    public bool ReleaseChannelWritable { get; set; } = true;

    public bool TryTransfer(string expectedPeer, string actualPeer, string expectedRun, string actualRun, bool inOriginalJob)
    {
        TransferAllowed = string.Equals(expectedPeer, actualPeer, StringComparison.Ordinal)
            && string.Equals(expectedRun, actualRun, StringComparison.Ordinal)
            && inOriginalJob;
        return TransferAllowed;
    }

    public bool TryExitRoot(bool adopted, bool released, bool guestReady, int duplicatedHandles)
    {
        if (!TransferAllowed || duplicatedHandles < 4 || !adopted || !released || !guestReady)
        {
            ExitRootSent = false;
            return false;
        }

        ExitRootSent = true;
        return true;
    }

    public void OnReleased(int stillOpenOriginalEnds)
    {
        OpenOriginalEndsAtReleased = stillOpenOriginalEnds;
    }

    public void OnAdopted(bool originalsStillOpen)
    {
        OriginalHandlesOpenUntilAdopted = originalsStillOpen;
    }
}
