// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.Contracts;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.ServiceModel.Channels
{
    internal enum HttpAbortReason
    {
        None,
        Aborted,
        TimedOut
    }

    internal static class HttpChannelUtilities
    {
        internal static class StatusDescriptionStrings
        {
            internal const string HttpContentTypeMissing = "Missing Content Type";
            internal const string HttpContentTypeMismatch = "Cannot process the message because the content type '{0}' was not the expected type '{1}'.";
            internal const string HttpStatusServiceActivationException = "System.ServiceModel.ServiceActivationException";
        }

        internal const string HttpStatusCodeExceptionKey = "System.ServiceModel.Channels.HttpInput.HttpStatusCode";
        internal const string HttpStatusDescriptionExceptionKey = "System.ServiceModel.Channels.HttpInput.HttpStatusDescription";
        internal const string HttpRequestHeadersTypeName = "System.Net.Http.Headers.HttpRequestHeaders";


        internal const int ResponseStreamExcerptSize = 1024;
        internal const string MIMEVersionHeader = "MIME-Version";
        internal const string ContentEncodingHeader = "Content-Encoding";

        internal const uint WININET_E_NAME_NOT_RESOLVED = 0x80072EE7;
        internal const uint WININET_E_CONNECTION_RESET = 0x80072EFF;
        internal const uint WININET_E_INCORRECT_HANDLE_STATE = 0x80072EF3;

        public static HttpResponseMessage ProcessGetResponseWebException(HttpRequestException requestException, HttpRequestMessage request, HttpAbortReason abortReason)
        {
            var inner = requestException.InnerException;
            if (inner != null)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(ConvertHttpRequestException(requestException, request, abortReason));
            }
            else
            {
                // There is no inner exception so there's not enough information to be able to convert to the correct WCF exception.
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new CommunicationException(requestException.Message, requestException));
            }
        }

        public static Exception ConvertHttpRequestException(HttpRequestException exception, HttpRequestMessage request, HttpAbortReason abortReason)
        {
            Contract.Assert(exception.InnerException != null, "InnerException must be set to be able to convert");

            uint hresult = (uint)exception.InnerException.HResult;
            switch (hresult)
            {
                // .Net Native HttpClientHandler sometimes reports an incorrect handle state when a connection is aborted, so we treat it as a connection reset error
                case WININET_E_INCORRECT_HANDLE_STATE:
                    goto case WININET_E_CONNECTION_RESET;
                case WININET_E_CONNECTION_RESET:
                    return new CommunicationException(SR.Format(SR.HttpReceiveFailure, request.RequestUri), exception);
                case WININET_E_NAME_NOT_RESOLVED:
                    return new EndpointNotFoundException(SR.Format(SR.EndpointNotFound, request.RequestUri.AbsoluteUri), exception);
                default:
                    return new CommunicationException(exception.Message, exception);
            }
        }

        internal static Exception CreateUnexpectedResponseException(HttpResponseMessage response)
        {
            string statusDescription = response.ReasonPhrase;
            if (string.IsNullOrEmpty(statusDescription))
                statusDescription = response.StatusCode.ToString();

            return TraceResponseException(
                new ProtocolException(SR.Format(SR.UnexpectedHttpResponseCode,
                (int)response.StatusCode, statusDescription)));
        }

        internal static string GetResponseStreamExcerptString(Stream responseStream, ref int bytesToRead)
        {
            long bufferSize = bytesToRead;

            if (bufferSize < 0 || bufferSize > ResponseStreamExcerptSize)
            {
                bufferSize = ResponseStreamExcerptSize;
            }

            byte[] responseBuffer = Fx.AllocateByteArray(checked((int)bufferSize));
            bytesToRead = responseStream.Read(responseBuffer, 0, (int)bufferSize);
            responseStream.Dispose();

            return Encoding.UTF8.GetString(responseBuffer, 0, bytesToRead);
        }

        internal static Exception TraceResponseException(Exception exception)
        {
            return exception;
        }

        internal static ProtocolException CreateHttpProtocolException(string message, HttpStatusCode statusCode, string statusDescription)
        {
            ProtocolException exception = new ProtocolException(message);
            exception.Data.Add(HttpChannelUtilities.HttpStatusCodeExceptionKey, statusCode);
            if (statusDescription != null && statusDescription.Length > 0)
            {
                exception.Data.Add(HttpChannelUtilities.HttpStatusDescriptionExceptionKey, statusDescription);
            }

            return exception;
        }
    }

    internal class PreReadStream : DelegatingStream
    {
        private byte[] _preReadBuffer;

        public PreReadStream(Stream stream, byte[] preReadBuffer)
            : base(stream)
        {
            _preReadBuffer = preReadBuffer;
        }

        private bool ReadFromBuffer(byte[] buffer, int offset, int count, out int bytesRead)
        {
            if (_preReadBuffer != null)
            {
                if (buffer == null)
                {
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("buffer");
                }

                if (offset >= buffer.Length)
                {
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new ArgumentOutOfRangeException("offset", offset,
                        SR.Format(SR.OffsetExceedsBufferBound, buffer.Length - 1)));
                }

                if (count < 0)
                {
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new ArgumentOutOfRangeException("count", count,
                        SR.ValueMustBeNonNegative));
                }

                if (count == 0)
                {
                    bytesRead = 0;
                }
                else
                {
                    buffer[offset] = _preReadBuffer[0];
                    _preReadBuffer = null;
                    bytesRead = 1;
                }

                return true;
            }

            bytesRead = -1;
            return false;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead;
            if (ReadFromBuffer(buffer, offset, count, out bytesRead))
            {
                return Task.FromResult<int>(bytesRead);
            }
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead;
            if (ReadFromBuffer(buffer, offset, count, out bytesRead))
            {
                return bytesRead;
            }

            return base.Read(buffer, offset, count);
        }

        public override int ReadByte()
        {
            if (_preReadBuffer != null)
            {
                byte[] tempBuffer = new byte[1];
                int bytesRead;
                if (ReadFromBuffer(tempBuffer, 0, 1, out bytesRead))
                {
                    return tempBuffer[0];
                }
            }
            return base.ReadByte();
        }
    }
}
