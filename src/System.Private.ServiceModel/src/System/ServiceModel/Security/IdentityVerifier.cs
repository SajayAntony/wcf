// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;
using System.IdentityModel.Claims;
using System.IdentityModel.Policy;
using System.Security.Principal;
using System.Collections.Generic;
using System.Globalization;
using System.ServiceModel.Diagnostics;
using System.ServiceModel.Diagnostics.Application;
using System.Runtime.Diagnostics;

namespace System.ServiceModel.Security
{
    public abstract class IdentityVerifier
    {
        protected IdentityVerifier()
        {
            // empty
        }
 
        public static IdentityVerifier CreateDefault()
        {
            return DefaultIdentityVerifier.Instance;
        }

        public abstract bool CheckAccess(EndpointIdentity identity, AuthorizationContext authContext);

        public abstract bool TryGetIdentity(EndpointAddress reference, out EndpointIdentity identity);

        static void AdjustAddress(ref EndpointAddress reference, Uri via)
        {
            // if we don't have an identity and we have differing Uris, we should use the Via
            if (reference.Identity == null && reference.Uri != via)
            {
                reference = new EndpointAddress(via);
            }
        }

        internal bool TryGetIdentity(EndpointAddress reference, Uri via, out EndpointIdentity identity)
        {
            AdjustAddress(ref reference, via);
            return this.TryGetIdentity(reference, out identity);
        }

        internal void EnsureOutgoingIdentity(EndpointAddress serviceReference, Uri via, AuthorizationContext authorizationContext)
        {
            AdjustAddress(ref serviceReference, via);
            this.EnsureIdentity(serviceReference, authorizationContext, SR.IdentityCheckFailedForOutgoingMessage);
        }
 
        internal void EnsureOutgoingIdentity(EndpointAddress serviceReference, ReadOnlyCollection<IAuthorizationPolicy> authorizationPolicies)
        {
            if (authorizationPolicies == null)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("authorizationPolicies");
            }
            AuthorizationContext ac = AuthorizationContext.CreateDefaultAuthorizationContext(authorizationPolicies);
            EnsureIdentity(serviceReference, ac, SR.IdentityCheckFailedForOutgoingMessage);
        }
 
        private void EnsureIdentity(EndpointAddress serviceReference, AuthorizationContext authorizationContext, String errorString)
        {
            if (authorizationContext == null)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("authorizationContext");
            }
            EndpointIdentity identity;
            if (!TryGetIdentity(serviceReference, out identity))
            {
                SecurityTraceRecordHelper.TraceIdentityVerificationFailure(identity, authorizationContext, this.GetType());
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperWarning(new MessageSecurityException(SR.Format(errorString, identity, serviceReference)));
            }
            else
            {
                if (!CheckAccess(identity, authorizationContext))
                {
                    // CheckAccess performs a Trace on failure, no need to do it twice
                    Exception e = CreateIdentityCheckException(identity, authorizationContext, errorString, serviceReference);
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperWarning(e);
                }
            }
        }

        private Exception CreateIdentityCheckException(EndpointIdentity identity, AuthorizationContext authorizationContext, string errorString, EndpointAddress serviceReference)
        {
            Exception result;
 
            if (identity.IdentityClaim != null
                && identity.IdentityClaim.ClaimType == ClaimTypes.Dns
                && identity.IdentityClaim.Right == Rights.PossessProperty
                && identity.IdentityClaim.Resource is string)
            {
                string expectedDnsName = (string)identity.IdentityClaim.Resource;
                string actualDnsName = null;
                for (int i = 0; i < authorizationContext.ClaimSets.Count; ++i)
                {
                    ClaimSet claimSet = authorizationContext.ClaimSets[i];
                    foreach (Claim claim in claimSet.FindClaims(ClaimTypes.Dns, Rights.PossessProperty))
                    {
                        if (claim.Resource is string)
                        {
                            actualDnsName = (string)claim.Resource;
                            break;
                        }
                    }
                    if (actualDnsName != null)
                    {
                        break;
                    }
                }
                if (SR.IdentityCheckFailedForIncomingMessage.Equals(errorString))
                {
                    if (actualDnsName == null)
                    {
                        result = new MessageSecurityException(SR.Format(SR.DnsIdentityCheckFailedForIncomingMessageLackOfDnsClaim, expectedDnsName));
                    }
                    else
                    {
                        result = new MessageSecurityException(SR.Format(SR.DnsIdentityCheckFailedForIncomingMessage, expectedDnsName, actualDnsName));
                    }
                }
                else if (SR.IdentityCheckFailedForOutgoingMessage.Equals(errorString))
                {
                    if (actualDnsName == null)
                    {
                        result = new MessageSecurityException(SR.Format(SR.DnsIdentityCheckFailedForOutgoingMessageLackOfDnsClaim, expectedDnsName));
                    }
                    else
                    {
                        result = new MessageSecurityException(SR.Format(SR.DnsIdentityCheckFailedForOutgoingMessage, expectedDnsName, actualDnsName));
                    }
                }
                else
                {
                    result = new MessageSecurityException(SR.Format(errorString, identity, serviceReference));
                }
            }
            else
            {
                result = new MessageSecurityException(SR.Format(errorString, identity, serviceReference));
            }
 
            return result;
        }

        private class DefaultIdentityVerifier : IdentityVerifier
        {
            static readonly DefaultIdentityVerifier instance = new DefaultIdentityVerifier();
 
            public static DefaultIdentityVerifier Instance
            {
                get { return instance; }
            }
 
            public override bool TryGetIdentity(EndpointAddress reference, out EndpointIdentity identity)
            {
                if (reference == null)
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("reference");
 
                identity = reference.Identity;
 
                if (identity == null)
                {
                    identity = this.TryCreateDnsIdentity(reference);
                }
 
                if (identity == null)
                {
                    SecurityTraceRecordHelper.TraceIdentityDeterminationFailure(reference, typeof(DefaultIdentityVerifier));
                    return false;
                }
                else
                {
                    SecurityTraceRecordHelper.TraceIdentityDeterminationSuccess(reference, identity, typeof(DefaultIdentityVerifier));
                    return true;
                }
            }

            EndpointIdentity TryCreateDnsIdentity(EndpointAddress reference)
            {
                Uri toAddress = reference.Uri;

                if (!toAddress.IsAbsoluteUri)
                    return null;

                return EndpointIdentity.CreateDnsIdentity(toAddress.DnsSafeHost);
            }

            internal Claim CheckDnsEquivalence(ClaimSet claimSet, string expectedSpn)
            {
                // host/<machine-name> satisfies the DNS identity claim
                IEnumerable<Claim> claims = claimSet.FindClaims(ClaimTypes.Spn, Rights.PossessProperty);
                foreach (Claim claim in claims)
                {
                    if (expectedSpn.Equals((string)claim.Resource, StringComparison.OrdinalIgnoreCase))
                    {
                        return claim;
                    }
                }
                return null;
            }

            public override bool CheckAccess(EndpointIdentity identity, AuthorizationContext authContext)
            {
                EventTraceActivity eventTraceActivity = null;
 
                if (identity == null)
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("identity");
 
                if (authContext == null)
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("authContext");
 
 
                if (FxTrace.Trace.IsEnd2EndActivityTracingEnabled)
                {
                    eventTraceActivity = EventTraceActivityHelper.TryExtractActivity((OperationContext.Current != null) ? OperationContext.Current.IncomingMessage : null);
                }
 
                for (int i = 0; i < authContext.ClaimSets.Count; ++i)
                {
                    ClaimSet claimSet = authContext.ClaimSets[i];
                    if (claimSet.ContainsClaim(identity.IdentityClaim))
                    {
                        SecurityTraceRecordHelper.TraceIdentityVerificationSuccess(eventTraceActivity, identity, identity.IdentityClaim, this.GetType());
                        return true;
                    }
 
                    // try Claim equivalence
                    string expectedSpn = null;
                    if (ClaimTypes.Dns.Equals(identity.IdentityClaim.ClaimType))
                    {
                        expectedSpn = string.Format(CultureInfo.InvariantCulture, "host/{0}", (string)identity.IdentityClaim.Resource);
                        Claim claim = CheckDnsEquivalence(claimSet, expectedSpn);
                        if (claim != null)
                        {
                            SecurityTraceRecordHelper.TraceIdentityVerificationSuccess(eventTraceActivity, identity, claim, this.GetType());
                            return true;
                        }
                    }

                    // Allow a Sid claim to support UPN, and SPN identities
                    
                    // SID claims not available yet 
                    //SecurityIdentifier identitySid = null;
                    //if (ClaimTypes.Sid.Equals(identity.IdentityClaim.ClaimType))
                    //{
                    //    throw ExceptionHelper.PlatformNotSupported("DefaultIdentityVerifier - ClaimTypes.Sid");
                    //}
                    //else if (ClaimTypes.Upn.Equals(identity.IdentityClaim.ClaimType))
                    //{
                    //    throw ExceptionHelper.PlatformNotSupported("DefaultIdentityVerifier - ClaimTypes.Upn");
                    //}
                    //else if (ClaimTypes.Spn.Equals(identity.IdentityClaim.ClaimType))
                    //{
                    //    throw ExceptionHelper.PlatformNotSupported("DefaultIdentityVerifier - ClaimTypes.Spn");
                    //}
                    //else if (ClaimTypes.Dns.Equals(identity.IdentityClaim.ClaimType))
                    //{
                    //    throw ExceptionHelper.PlatformNotSupported("DefaultIdentityVerifier - ClaimTypes.Dns");
                    //}
                    //if (identitySid != null)
                    //{
                    //    Claim claim = CheckSidEquivalence(identitySid, claimSet);
                    //    if (claim != null)
                    //    {
                    //        SecurityTraceRecordHelper.TraceIdentityVerificationSuccess(eventTraceActivity, identity, claim, this.GetType());
                    //        return true;
                    //    }
                    //}
                }
                SecurityTraceRecordHelper.TraceIdentityVerificationFailure(identity, authContext, this.GetType());
                if (TD.SecurityIdentityVerificationFailureIsEnabled())
                {
                    TD.SecurityIdentityVerificationFailure(eventTraceActivity);
                }

                return false; 
            }
        }
    }
}
