﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using WcfTestBridgeCommon;

namespace Bridge
{
    public static class AppDomainManager
    {
        // This method is called whenever the resource folder location changes.
        // Null is allowed and means there is no resource folder.
        public static string OnResourceFolderChanged(string oldFolder, string newFolder)
        {
            // Changing the current AppDomain's folder to null unloads the AppDomain
            if (!String.Equals(oldFolder, newFolder))
            {
                if (!(String.IsNullOrEmpty(ConfigController.CurrentAppDomainName)) && newFolder == null)
                {
                    Trace.WriteLine(String.Format("{0:T} Shutting down the appDomain for {1}", DateTime.Now, ConfigController.CurrentAppDomainName),
                                    typeof(AppDomainManager).Name);
                    ShutdownAppDomain(ConfigController.CurrentAppDomainName);
                }
            }

            if (newFolder == null)
            {
                return null;
            }

            var newPath = Path.GetFullPath(newFolder);
            Trace.WriteLine(String.Format("{0:T} Adding assemblies from the resource folder {1}", DateTime.Now, newPath), 
                            typeof(AppDomainManager).Name);
            return CreateAppDomain(newPath);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static string CreateAppDomain(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException("path");
            }

            string friendlyName = "BridgeAppDomain" + TypeCache.AppDomains.Count;
            var appDomainSetup = new AppDomainSetup();
            appDomainSetup.ApplicationBase = path;
            var newAppDomain = AppDomain.CreateDomain(friendlyName, AppDomain.CurrentDomain.Evidence, appDomainSetup);

            Type loaderType = typeof(AssemblyLoader);
            var loader =
                (AssemblyLoader)newAppDomain.CreateInstanceFromAndUnwrap(
                    Path.Combine(path, "WcfTestBridgeCommon.dll"),
                    loaderType.FullName);
            loader.LoadAssemblies();

            TypeCache.AppDomains.Add(friendlyName, newAppDomain);
            TypeCache.Cache.Add(friendlyName, loader.GetTypes());

            Trace.WriteLine(String.Format("{0:T} - Created new AppDomain '{1}'", DateTime.Now, friendlyName),
                            typeof(AppDomainManager).Name);

            return friendlyName;
        }

        public static void ShutdownAllAppDomains()
        {
            foreach (string domainName in TypeCache.AppDomains.Keys.ToArray())
            {
                ShutdownAppDomain(domainName);
            }

            ConfigController.CurrentAppDomainName = null;
        }

        public static void ShutdownAppDomain(string appDomainName)
        {
            if (String.IsNullOrWhiteSpace(appDomainName))
            {
                throw new ArgumentNullException("appDomainName");
            }

            lock (ConfigController.ConfigLock)
            {
                AppDomain appDomain = null;
                if (TypeCache.AppDomains.TryGetValue(appDomainName, out appDomain))
                {
                    // If the AppDomain cannot unload, allow the exception to propagate
                    // back to the caller and leave the current cache state unaffected.
                    AppDomain.Unload(appDomain);
                    TypeCache.AppDomains.Remove(appDomainName);
                    TypeCache.Cache.Remove(appDomainName);
                    Trace.WriteLine(String.Format("{0:T} - Shutdown AppDomain '{1}'", DateTime.Now, appDomainName),
                                    typeof(AppDomainManager).Name);
                }

                // If this is the main AppDomain known to hold resources,
                // reset to null to avoid further use.
                if (String.Equals(appDomainName, ConfigController.CurrentAppDomainName, StringComparison.OrdinalIgnoreCase))
                {
                    ConfigController.CurrentAppDomainName = null;
                }
            }
        }
    }

    internal static class TypeCache
    {
        private static Dictionary<string, AppDomain> s_appDomains = new Dictionary<string, AppDomain>();
        private static IDictionary<string, List<string>> s_cache = new Dictionary<string, List<string>>();

        public static Dictionary<string, AppDomain> AppDomains { get { return s_appDomains; } }

        public static IDictionary<string, List<string>> Cache { get { return s_cache; } }
    }
}
