using System;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using System.Collections.Generic;
using System.Xml;
using Newtonsoft.Json;
using Azure.Messaging.ServiceBus;
using System.Security;
using System.Security.Permissions;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AllowPartiallyTrustedCallers]
namespace BD.CRM
{

    public class ASBPlugin365 : IPlugin
    {
        private readonly string _unsecureString;
        private readonly string _secureString;

        private XmlDocument _secureConfig;
        private XmlDocument _unsecureConfig;

        private string ASB_ConnectionString = "";
        private string ASB_Queue = "";
        private string ASB_Message = "";

        public static string GetConfigDataString(XmlDocument doc, string label)
        {
            return GetValueNode(doc, label);
        }
        private static string GetValueNode(XmlDocument doc, string key)
        {
            XmlNode node = doc.SelectSingleNode(String.Format("Settings/setting[@name='{0}']", key));
            if (node != null)
            {
                return node.SelectSingleNode("value").InnerText;
            }
            return string.Empty;
        }

        public ASBPlugin365(string unsecureString, string secureString)
        {
            if (String.IsNullOrWhiteSpace(unsecureString))
            {
                throw new InvalidOperationException("config string is required but not provided.");
            }

            _unsecureString = unsecureString;
            _secureString = secureString;

            _unsecureConfig = new XmlDocument();
            _unsecureConfig.LoadXml(_unsecureString);

            ASB_ConnectionString = GetValueNode(_unsecureConfig, "connectionstring");

            if (String.IsNullOrWhiteSpace(ASB_ConnectionString))
            {
                throw new InvalidOperationException("Connection String is not provided");
            }

        }
        public void Execute(IServiceProvider serviceProvider)
        {
            //<snippetAdvancedPlugin2>
            //Extract the tracing service for use in debugging sandboxed plug-ins.
            ITracingService tracingService =
                (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Obtain the execution context from the service provider.
            IPluginExecutionContext context = (IPluginExecutionContext)
                serviceProvider.GetService(typeof(IPluginExecutionContext));

            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            tracingService.Trace("ASBPlugin: Verifying the client is not offline.");
            if (context.IsExecutingOffline || context.IsOfflinePlayback)
                return;

            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
            {
                // Obtain the target entity from the Input Parameters.
                tracingService.Trace("ASBPlugin: Getting the target entity from Input Parameters.");
                Entity entity = (Entity)context.InputParameters["Target"];

                var _EntityLogicalName = entity.LogicalName;
                var _EntityRecordId = entity.Id;

                Entity FullEntity = (Entity)service.Retrieve(_EntityLogicalName, _EntityRecordId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));

                try
                {
                    ASB_Message = JsonConvert.SerializeObject(FullEntity);
                    ASB_Queue = entity.LogicalName;

                    try
                    {
                        var sb = new ServiceBusClient(ASB_ConnectionString);
                        var q = sb.CreateSender(ASB_Queue);
                        var msg = new ServiceBusMessage(ASB_Message);
                        msg.Subject = _EntityLogicalName + "," + _EntityRecordId.ToString();
                        msg.ContentType = "application/json";

                        q.SendMessageAsync(msg);
                    }
                    catch (Exception ex)
                    {
                        tracingService.Trace("ASBPlugin: Unable to send message to queue ");
                        tracingService.Trace(ASB_Queue);
                        tracingService.Trace(ex.Message);
                        throw new InvalidOperationException("ASB Send Failed");
                    }
                }
                catch (Exception ex)
                {
                    tracingService.Trace("ASBPlugin: Unable to serialize entity object (" + ex.Message + ")");
                    throw new InvalidOperationException("Object Serialization Failed");
                }

                tracingService.Trace("ASBPlugin: End Plugin Execution");
            }
        }
    }
}

