using Windows.ApplicationModel;
using Windows.Services.Store;

namespace Itmam.Win32;

/// <summary>Result of asking the Microsoft Store for a newer version.</summary>
public enum UpdateCheck
{
    /// <summary>Not installed from a package, or the Store could not answer (offline, Store disabled).</summary>
    Unavailable,
    UpToDate,
    UpdateAvailable,
}

/// <summary>Result of the Store's rate and review window.</summary>
public enum RateAndReview
{
    /// <summary>The user sent a rating or review, or closed the window.</summary>
    Done,

    /// <summary>Not installed from the Store, or the Store could not be reached.</summary>
    Unavailable,
}

/// <summary>
/// Update check through the Microsoft Store (user decision). <see cref="StoreContext"/> asks the Store
/// service in Windows, which does the network work: Itmam itself keeps no network capability and sends
/// nothing but the question "is there a newer version of this package?". Works only for the Store
/// installation; development and side-loaded test builds get <see cref="UpdateCheck.Unavailable"/>
/// or <see cref="UpdateCheck.UpToDate"/>.
/// </summary>
public static class StoreUpdates
{
    /// <summary>Partner Center product 9P08F7KGRJB0 (Package.appxmanifest identity).</summary>
    public const string ProductId = "9P08F7KGRJB0";

    /// <summary>Itmam's page in the Microsoft Store app.</summary>
    public static Uri StorePage { get; } = new($"ms-windows-store://pdp/?productid={ProductId}");

    /// <summary>The package version ("1.1.0") when packaged, otherwise null.</summary>
    public static string? PackageVersion
    {
        get
        {
            if (!StartupRegistration.IsPackaged)
            {
                return null;
            }

            var v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>
    /// The Store's own rate and review window, over <paramref name="owner"/> (user decision after 2.1). Only
    /// when the user presses the button: the Store service does the network work, and whatever is written in
    /// that window goes from the user to the Store, not through Itmam.
    /// </summary>
    public static async Task<RateAndReview> RequestRateAndReviewAsync(nint owner)
    {
        if (!StartupRegistration.IsPackaged)
        {
            return RateAndReview.Unavailable;
        }

        try
        {
            var context = StoreContext.GetDefault();
            WinRT.Interop.InitializeWithWindow.Initialize(context, owner); // a desktop app must name its window
            var result = await context.RequestRateAndReviewAppAsync();
            return result.Status is StoreRateAndReviewStatus.Succeeded or StoreRateAndReviewStatus.CanceledByUser
                ? RateAndReview.Done
                : RateAndReview.Unavailable;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            return RateAndReview.Unavailable;
        }
    }

    public static async Task<UpdateCheck> CheckAsync()
    {
        if (!StartupRegistration.IsPackaged)
        {
            return UpdateCheck.Unavailable;
        }

        try
        {
            var updates = await StoreContext.GetDefault().GetAppAndOptionalStorePackageUpdatesAsync();
            return updates.Count > 0 ? UpdateCheck.UpdateAvailable : UpdateCheck.UpToDate;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            return UpdateCheck.Unavailable; // offline, Store blocked by policy, or not a Store installation
        }
    }
}
