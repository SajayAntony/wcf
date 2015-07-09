// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceModel;
using System.Text;
using Xunit;

public static class ExpectedExceptionTests
{
    [Fact]
    [OuterLoop]
    public static void NotExistentHost_Throws_EndpointNotFoundException()
    {
        string nonExistentHost = "http://nonexisthost/WcfService/WindowsCommunicationFoundation";

        BasicHttpBinding binding = new BasicHttpBinding();
        binding.SendTimeout = TimeSpan.FromMilliseconds(10000);

        EndpointNotFoundException exception = Assert.Throws<EndpointNotFoundException>(() =>
        {
            using (ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(nonExistentHost)))
            {
                IWcfService serviceProxy = factory.CreateChannel();
                string response = serviceProxy.Echo("Hello");
            }
        });

        // On .Net Native retail, exception message is stripped to include only parameter
        Assert.Contains(nonExistentHost, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [OuterLoop]
    public static void ServiceRestart_Throws_CommunicationException()
    {
        StringBuilder errorBuilder = new StringBuilder();
        string restartServiceAddress = "";

        BasicHttpBinding binding = new BasicHttpBinding();

        try
        {
            using (ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(Endpoints.HttpBaseAddress_Basic)))
            {
                IWcfService serviceProxy = factory.CreateChannel();
                restartServiceAddress = serviceProxy.GetRestartServiceEndpoint();
            }
        }
        catch (Exception e)
        {
            string error = String.Format("Unexpected exception thrown while calling the 'GetRestartServiceEndpoint' operation. {0}", e.ToString());
            if (e.InnerException != null)
                error += String.Format("\r\nInnerException:\r\n{0}", e.InnerException.ToString());
            errorBuilder.AppendLine(error);
        }

        if (errorBuilder.Length == 0)
        {
            // Get the Service host name and replace localhost with it
            UriBuilder builder = new UriBuilder(Endpoints.HttpBaseAddress_Basic);
            string hostName = builder.Uri.Host;
            restartServiceAddress = restartServiceAddress.Replace("[HOST]", hostName);
            //On .NET Native retail, exception message is stripped to include only parameter
            string expectExceptionMsg = restartServiceAddress;

            try
            {
                using (ChannelFactory<IWcfRestartService> factory = new ChannelFactory<IWcfRestartService>(binding, new EndpointAddress(restartServiceAddress)))
                {
                    // Get the last portion of the restart service url which is a Guid and convert it back to a Guid
                    // This is needed by the RestartService operation as a Dictionary key to get the ServiceHost
                    string uniqueIdentifier = restartServiceAddress.Substring(restartServiceAddress.LastIndexOf("/") + 1);
                    Guid guid = new Guid(uniqueIdentifier);

                    IWcfRestartService serviceProxy = factory.CreateChannel();
                    serviceProxy.RestartService(guid);
                }

                errorBuilder.AppendLine("Expected CommunicationException exception, but no exception thrown.");
            }
            catch (Exception e)
            {
                if (e.GetType() == typeof(CommunicationException))
                {
                    if (e.Message.Contains(expectExceptionMsg))
                    {
                    }
                    else
                    {
                        errorBuilder.AppendLine(string.Format("Expected exception message contains: {0}, actual: {1}", expectExceptionMsg, e.Message));
                    }
                }
                else
                {
                    errorBuilder.AppendLine(string.Format("Expected exception: {0}, actual: {1}/n Exception was: {2}", "CommunicationException", e.GetType(), e.ToString()));
                }
            }
        }

        Assert.True(errorBuilder.Length == 0, string.Format("Test Scenario: ServiceRestart_Throws_CommunicationException FAILED with the following errors: {0}", errorBuilder));
    }

    [Fact]
    [OuterLoop]
    public static void NonExistentAction_Throws_ActionNotSupportedException()
    {
        string exceptionMsg = "The message with Action 'http://tempuri.org/IWcfService/NotExistOnServer' cannot be processed at the receiver, due to a ContractFilter mismatch at the EndpointDispatcher. This may be because of either a contract mismatch (mismatched Actions between sender and receiver) or a binding/security mismatch between the sender and the receiver.  Check that sender and receiver have the same contract and the same binding (including security requirements, e.g. Message, Transport, None).";
        try
        {
            BasicHttpBinding binding = new BasicHttpBinding();
            using (ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(Endpoints.HttpBaseAddress_Basic)))
            {
                IWcfService serviceProxy = factory.CreateChannel();
                serviceProxy.NotExistOnServer();
            }
        }
        catch (Exception e)
        {
            if (e.GetType() != typeof(System.ServiceModel.ActionNotSupportedException))
            {
                Assert.True(false, string.Format("Expected exception: {0}, actual: {1}", "ActionNotSupportedException", e.GetType()));
            }

            if (e.Message != exceptionMsg)
            {
                Assert.True(false, string.Format("Expected Fault Message: {0}, actual: {1}", exceptionMsg, e.Message));
            }
            return;
        }

        Assert.True(false, "Expected ActionNotSupportedException exception, but no exception thrown.");
    }

    // SendTimeout is set to 5 seconds, the service waits 10 seconds to respond.
    // The client should throw a TimeoutException
    [Fact]
    [OuterLoop]
    public static void SendTimeout_For_Long_Running_Operation_Throws_TimeoutException()
    {
        BasicHttpBinding binding = new BasicHttpBinding();
        binding.SendTimeout = TimeSpan.FromMilliseconds(5000);
        ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(Endpoints.HttpBaseAddress_Basic));

        Stopwatch watch = new Stopwatch();
        try
        {
            var exception = Assert.Throws<TimeoutException>(() =>
            {
                IWcfService proxy = factory.CreateChannel();
                watch.Start();
                proxy.EchoWithTimeout("Hello");
            });
        }
        finally
        {
            watch.Stop();
        }

        // want to assert that this completed in > 5 s as an upper bound since the SendTimeout is 5 sec
        // (usual case is around 5001-5005 ms) 
        Assert.InRange<long>(watch.ElapsedMilliseconds, 5000, 10000);
    }

    // SendTimeout is set to 0, this should trigger a TimeoutException before even attempting to call the service.
    [Fact]
    [OuterLoop]
    public static void SendTimeout_Zero_Throws_TimeoutException_Immediately()
    {
        BasicHttpBinding binding = new BasicHttpBinding();
        binding.SendTimeout = TimeSpan.FromMilliseconds(0);
        ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(Endpoints.HttpBaseAddress_Basic));

        Stopwatch watch = new Stopwatch();
        try
        {
            var exception = Assert.Throws<TimeoutException>(() =>
            {
                IWcfService proxy = factory.CreateChannel();
                watch.Start();
                proxy.EchoWithTimeout("Hello");
            });
        }
        finally
        {
            watch.Stop(); 
        }

        // want to assert that this completed in < 2 s as an upper bound since the SendTimeout is 0 sec
        // (usual case is around 1 - 3 ms) 
        Assert.InRange<long>(watch.ElapsedMilliseconds, 0, 2000);
    }

    [Fact]
    [OuterLoop]
    public static void FaultException_Throws_WithFaultDetail()
    {
        string faultMsg = "Test Fault Exception";
        StringBuilder errorBuilder = new StringBuilder();

        try
        {
            BasicHttpBinding binding = new BasicHttpBinding();
            using (ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(Endpoints.HttpBaseAddress_Basic)))
            {
                IWcfService serviceProxy = factory.CreateChannel();
                serviceProxy.TestFault(faultMsg);
            }
        }
        catch (Exception e)
        {
            if (e.GetType() != typeof(FaultException<FaultDetail>))
            {
                string error = string.Format("Expected exception: {0}, actual: {1}\r\n{2}",
                                             "FaultException<FaultDetail>", e.GetType(), e.ToString());
                if (e.InnerException != null)
                    error += String.Format("\r\nInnerException:\r\n{0}", e.InnerException.ToString());
                errorBuilder.AppendLine(error);
            }
            else
            {
                FaultException<FaultDetail> faultException = (FaultException<FaultDetail>)(e);
                string actualFaultMsg = ((FaultDetail)(faultException.Detail)).Message;
                if (actualFaultMsg != faultMsg)
                {
                    errorBuilder.AppendLine(string.Format("Expected Fault Message: {0}, actual: {1}", faultMsg, actualFaultMsg));
                }
            }

            Assert.True(errorBuilder.Length == 0, string.Format("Test Scenario: FaultException_Throws_WithFaultDetail FAILED with the following errors: {0}", errorBuilder));
            return;
        }

        Assert.True(false, "Expected FaultException<FaultDetail> exception, but no exception thrown.");
    }

    [Fact]
    [OuterLoop]
    public static void UnexpectedException_Throws_FaultException()
    {
        string faultMsg = "This is a test fault msg";
        StringBuilder errorBuilder = new StringBuilder();

        try
        {
            BasicHttpBinding binding = new BasicHttpBinding();
            using (ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(Endpoints.HttpBaseAddress_Basic)))
            {
                IWcfService serviceProxy = factory.CreateChannel();
                serviceProxy.ThrowInvalidOperationException(faultMsg);
            }
        }
        catch (Exception e)
        {
            if (e.GetType() != typeof(FaultException<ExceptionDetail>))
            {
                errorBuilder.AppendLine(string.Format("Expected exception: {0}, actual: {1}", "FaultException<ExceptionDetail>", e.GetType()));
            }
            else
            {
                FaultException<ExceptionDetail> faultException = (FaultException<ExceptionDetail>)(e);
                string actualFaultMsg = ((ExceptionDetail)(faultException.Detail)).Message;
                if (actualFaultMsg != faultMsg)
                {
                    errorBuilder.AppendLine(string.Format("Expected Fault Message: {0}, actual: {1}", faultMsg, actualFaultMsg));
                }
            }

            Assert.True(errorBuilder.Length == 0, string.Format("Test Scenario: UnexpectedException_Throws_FaultException FAILED with the following errors: {0}", errorBuilder));
            return;
        }

        Assert.True(false, "Expected FaultException<FaultDetail> exception, but no exception thrown.");
    }

    [Fact]
    [OuterLoop]
    public static void UnknownUrl_Throws_EndpointNotFoundException()
    {
        string notFoundUrl = Endpoints.HttpUrlNotFound_Address;

        BasicHttpBinding binding = new BasicHttpBinding();
        binding.SendTimeout = TimeSpan.FromMilliseconds(10000);

        EndpointNotFoundException exception = Assert.Throws<EndpointNotFoundException>(() =>
        {
            using (ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(notFoundUrl)))
            {
                IWcfService serviceProxy = factory.CreateChannel();
                string response = serviceProxy.Echo("Hello");
            }
        });

        // On .Net Native retail, exception message is stripped to include only parameter
        Assert.Contains(notFoundUrl, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [OuterLoop]
    public static void UnknownUrl_Throws_ProtocolException()
    { 
        string protocolExceptionUri = Endpoints.HttpProtocolError_Address;

        BasicHttpBinding binding = new BasicHttpBinding();
        binding.SendTimeout = TimeSpan.FromMilliseconds(10000);

        using (ChannelFactory<IWcfService> factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(protocolExceptionUri)))
        {
            IWcfService serviceProxy = factory.CreateChannel();
            ProtocolException exception = Assert.Throws<ProtocolException>(() =>
            {
                string response = serviceProxy.Echo("Hello");
            });

            // On .Net Native retail, exception message is stripped to include only parameter
            Assert.Contains(protocolExceptionUri, exception.Message);
        }
    }
}
