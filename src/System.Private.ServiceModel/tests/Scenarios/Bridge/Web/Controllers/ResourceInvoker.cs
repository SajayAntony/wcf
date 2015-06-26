﻿using System;
using System.Diagnostics;
using WcfTestBridgeCommon;
using Web.Models;
using Web.Models.Data;

namespace Web.Controllers
{
    public class ResourceInvoker
    {
        public static object DynamicInvoke(resource resource)
        {
            if (String.IsNullOrEmpty(resource.name))
            {
                throw new ArgumentNullException("resource.name");
            }

            AppDomain appDomain;
            if (!TypeCache.AppDomains.TryGetValue(ConfigController.CurrentAppDomain, out appDomain))
            {
                throw new ArgumentException("Resource not found");
            }

            Type loaderType = typeof(AssemblyLoader);
            var loader =
                (AssemblyLoader)appDomain.CreateInstanceFromAndUnwrap(
                    loaderType.Assembly.Location,
                    loaderType.FullName);
            loader.LoadAssemblies();

            return loader.IResourcePUT(resource.name);
        }
    }
}
