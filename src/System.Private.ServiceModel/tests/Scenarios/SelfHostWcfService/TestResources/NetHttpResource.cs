﻿using System.ServiceModel;
using System.ServiceModel.Channels;

namespace WcfService.TestResources
{
    internal class NetHttpResource : ResourceController<WcfService, IWcfService>
    {
        protected override string Protocol { get { return "http"; } }

        protected override string Address { get { return "NetHttp"; } }

        protected override string Port { get { return "8081"; } }

        protected override Binding GetBinding()
        {
            return new NetHttpBinding();
        }
    }
}
