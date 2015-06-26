﻿using System.ServiceModel;
using System.ServiceModel.Channels;

namespace WcfService.TestResources
{
    internal class BasicHttpResource : ResourceController<WcfService, IWcfService>
    {
        protected override string Protocol { get { return "http"; } }

        protected override string Address { get { return "Basic"; } }

        protected override string Port { get { return "8081"; } }

        protected override Binding GetBinding()
        {
            return new BasicHttpBinding();
        }
    }
}
