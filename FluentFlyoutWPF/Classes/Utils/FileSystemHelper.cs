// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Downstream;
using System.IO;
using Windows.Storage;

namespace FluentFlyoutWPF.Classes.Utils
{
    internal class FileSystemHelper
    {
        public static string GetLogsPath()
        {
            string path;

            // the way MSIX apps choose a logs path work incredibly weirdly (i haven't figured it out), so we're searching multiple possible locations
            // first, check same path as where settings is saved
            try
            {
                path = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path,
                    "Roaming",
                    ProductIdentity.Slug);
                if (Directory.Exists(path))
                    return path;
            }
            catch { }

            // if that doesn't work, check the downstream AppData directory
            try
            {
                path = ProductIdentity.AppDataDirectoryPath;
                if (Directory.Exists(path))
                    return path;
            }
            catch { }

            // Return the same downstream path used by SettingsManager when no
            // packaged cache directory has been created yet.
            return ProductIdentity.AppDataDirectoryPath;
        }
    }
}