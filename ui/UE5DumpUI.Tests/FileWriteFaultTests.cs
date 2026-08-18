using System;
using System.IO;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pins the X12 classifier that decides when Install-CE-autorun takes its manual
/// save-dialog fallback: a denied/failed write to CE's install folder (commonly
/// under %ProgramFiles%, needing elevation) must be treated as "not writable" so
/// the fallback runs — while unrelated programming errors (and cancellation) must
/// NOT be swallowed as a write-denied.
/// </summary>
public class FileWriteFaultTests
{
    [Fact]
    public void UnauthorizedAccess_IsPlacementDenied()
        => Assert.True(FileWriteFault.IsPlacementDenied(new UnauthorizedAccessException()));

    [Fact]
    public void GenericIOException_IsPlacementDenied()
        => Assert.True(FileWriteFault.IsPlacementDenied(new IOException("sharing violation")));

    [Fact]
    public void DirectoryNotFound_IsPlacementDenied_ItSubclassesIOException()
        => Assert.True(FileWriteFault.IsPlacementDenied(new DirectoryNotFoundException()));

    [Fact]
    public void SecurityException_IsPlacementDenied()
        => Assert.True(FileWriteFault.IsPlacementDenied(new System.Security.SecurityException()));

    [Fact]
    public void NegativeControl_UnrelatedError_IsNotSwallowed()
    {
        Assert.False(FileWriteFault.IsPlacementDenied(new InvalidOperationException()));
        Assert.False(FileWriteFault.IsPlacementDenied(new ArgumentNullException()));
    }

    [Fact]
    public void NegativeControl_Cancellation_IsNotTreatedAsWriteDenied()
        => Assert.False(FileWriteFault.IsPlacementDenied(new OperationCanceledException()));
}
