﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// 

using System.ServiceModel.Channels;

namespace System.ServiceModel
{
    public class BasicHttpsBinding : HttpBindingBase
    {
        WSMessageEncoding _messageEncoding = BasicHttpBindingDefaults.MessageEncoding;
        private BasicHttpsSecurity _basicHttpsSecurity;

        public BasicHttpsBinding() : this(BasicHttpsSecurity.DefaultMode) { }

        public BasicHttpsBinding(BasicHttpsSecurityMode securityMode)
        {
            if (securityMode == BasicHttpsSecurityMode.TransportWithMessageCredential)
            {
                throw ExceptionHelper.PlatformNotSupported(SR.Format(SR.UnsupportedSecuritySetting, "securityMode", securityMode));
            }

            _basicHttpsSecurity = new BasicHttpsSecurity();
            _basicHttpsSecurity.Mode = securityMode;
        }

        internal WSMessageEncoding MessageEncoding
        {
            get { return _messageEncoding; }
            set { _messageEncoding = value; }
        }

        public BasicHttpsSecurity Security
        {
            get
            {
                return _basicHttpsSecurity;
            }

            set
            {
                if (value == null)
                {
                    throw FxTrace.Exception.ArgumentNull("value");
                }

                _basicHttpsSecurity = value;
            }
        }

        internal override BasicHttpSecurity BasicHttpSecurity
        {
            get
            {
                return _basicHttpsSecurity.BasicHttpSecurity;
            }
        }

        internal override EnvelopeVersion GetEnvelopeVersion()
        {
            return EnvelopeVersion.Soap11;
        }

        public override IChannelFactory<TChannel> BuildChannelFactory<TChannel>(BindingParameterCollection parameters)
        {
            if ((BasicHttpSecurity.Mode == BasicHttpSecurityMode.Transport ||
                BasicHttpSecurity.Mode == BasicHttpSecurityMode.TransportCredentialOnly) &&
                BasicHttpSecurity.Transport.ClientCredentialType == HttpClientCredentialType.InheritedFromHost)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SR.Format(SR.HttpClientCredentialTypeInvalid, BasicHttpSecurity.Transport.ClientCredentialType)));
            }

            return base.BuildChannelFactory<TChannel>(parameters);
        }

        public override BindingElementCollection CreateBindingElements()
        {
            CheckSettings();

            // return collection of BindingElements
            BindingElementCollection bindingElements = new BindingElementCollection();
            // order of BindingElements is important
            // add security (*optional)
            SecurityBindingElement wsSecurity = BasicHttpSecurity.CreateMessageSecurity();
            if (wsSecurity != null)
            {
                bindingElements.Add(wsSecurity);
            }
            // add encoding
            if (MessageEncoding == WSMessageEncoding.Text)
                bindingElements.Add(TextMessageEncodingBindingElement);
            // add transport (http or https)
            bindingElements.Add(GetTransport());

            return bindingElements.Clone();
        }
    }
}