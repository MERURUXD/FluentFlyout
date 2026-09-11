// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Windows;
using System.Windows.Interop;
using Windows.Services.Store;

namespace FluentFlyout.Classes;

/// <summary>
/// Manages app licensing and premium features through the Microsoft Store
/// </summary>
public class LicenseManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static LicenseManager? _instance;
    private static readonly object _lock = new();

    private StoreContext? _storeContext;
    private StoreAppLicense? _appLicense;

    private const string PremiumOtpAddonOnId = "9N3XXQFPGFW5";
    private const string PremiumSubscriptionAddonOnId = "9P75DCVR4FRC";

    private bool _isInitialized;
    private bool _isPremiumUnlocked;
    private bool _isStoreVersion;

    /// <summary>
    /// Gets the singleton instance of the LicenseManager
    /// </summary>
    public static LicenseManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new LicenseManager();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Gets whether the app is a Store version (has Store Product ID)
    /// </summary>
    public bool IsStoreVersion => _isStoreVersion;

    /// <summary>
    /// Gets whether premium features are unlocked
    /// </summary>
    public bool IsPremiumUnlocked => _isPremiumUnlocked;

    private LicenseManager()
    {
        _isInitialized = false;
        _isPremiumUnlocked = false;
    }

    /// <summary>
    /// Initializes the license manager and checks license status
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        try
        {
            Logger.Info("LicenseManager: Initializing");
#if GITHUB_RELEASE
            _isStoreVersion = false;
            _isPremiumUnlocked = true;
            _isInitialized = true;
            return;
#endif
            // Get Store context
            _storeContext = StoreContext.GetDefault();

            var interop = new WindowInteropHelper(Application.Current.MainWindow);
            IntPtr hwnd = interop.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(_storeContext, hwnd);

            // Get app license
            _appLicense = await _storeContext.GetAppLicenseAsync();

            _isStoreVersion = !string.IsNullOrEmpty(_appLicense?.SkuStoreId);

            if (!_isStoreVersion)
            {
                // Self-compiled or GitHub version - unlock premium for free
                Logger.Info("Non-Store version detected. Premium unlocked.");

                _isPremiumUnlocked = true;
            }
            else
            {
                // Store version - check if premium add-on is purchased
                Logger.Info("Store version detected (SKU: {Sku})", _appLicense?.SkuStoreId);
                await CheckPremiumStatusAsync();
            }

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error initializing");
            _isInitialized = true;
        }
    }

    /// <summary>
    /// Checks if the premium add-on is purchased
    /// </summary>
    private async Task CheckPremiumStatusAsync()
    {
        try
        {
            if (_storeContext == null)
                return;

            // works offline
            if (_appLicense == null)
                _appLicense = await _storeContext.GetAppLicenseAsync();

            if (_appLicense == null)
            {
                Logger.Warn("App license is null");
                return;
            }

            _isPremiumUnlocked = false;

            // check for premium
            // If the user has purchased either the premium OTP or the subscription, we consider premium unlocked
            foreach (var addOnLicense in _appLicense.AddOnLicenses)
            {
                StoreLicense license = addOnLicense.Value;

                bool isPremiumSku =
                    license.SkuStoreId.Contains(PremiumOtpAddonOnId, StringComparison.OrdinalIgnoreCase) ||
                    license.SkuStoreId.Contains(PremiumSubscriptionAddonOnId, StringComparison.OrdinalIgnoreCase);

                if (!isPremiumSku)
                    continue;

                if (license.IsActive)
                {
                    _isPremiumUnlocked = true;
                    Logger.Info($"Premium unlocked via SKU {license.SkuStoreId}");
                    return;
                }
            }

            Logger.Debug("Premium not owned by user.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error checking premium status");
        }
    }

    /// <summary>
    /// Refreshes the license status (checks for changes)
    /// </summary>
    public async Task RefreshLicenseAsync()
    {
        if (!_isStoreVersion)
            return;

        await CheckPremiumStatusAsync();
        SettingsManager.Current.IsPremiumUnlocked = _isPremiumUnlocked;
    }

}