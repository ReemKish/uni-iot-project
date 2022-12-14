using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace Arc.Function
{
    public static class FallDetection
    {
        static string HUB_NAME = "arc-notification-hub";
        static string NH_NAMESPACE = "arc-notification-hub-namespace";
        static readonly HttpClient httpClient = new HttpClient();

        public static string getNotificationString(string ownerName, string deviceId){
            JObject jnotification = new JObject();
            jnotification.Add("title", "ARC ALERT!"); 
            jnotification.Add("body", $"{ownerName}'s ARC device has detected a fall!"); 

            JObject jdata = new JObject();                        
            jdata.Add("deviceId", deviceId); 

            JObject jobject = new JObject();
            jobject.Add("notification", jnotification); 
            jobject.Add("data", jdata); 
            return jobject.ToString();
        }

        [FunctionName("FallDetection")]
        public static async Task Run([EventHubTrigger("fall-detection-event-hub", Connection = "eventhubworkshop2022_iothubroutes_iothubworkshop082022_EVENTHUB")] EventData[] events, ILogger log,
        [CosmosDB(
                databaseName: "arc_db_id",
                collectionName: "users",
                ConnectionStringSetting = "CosmosDbConnectionString")] DocumentClient client,
        [CosmosDB(
                databaseName: "arc_db_id",
                collectionName: "fallevents",
                ConnectionStringSetting = "CosmosDbConnectionString")]IAsyncCollector<dynamic> fallEventsDocumentsOut)
        {
            var exceptions = new List<Exception>();

            foreach (EventData eventData in events)
            {
                try
                {
                    // Get the device id from event
                    string eventBody = Encoding.UTF8.GetString(eventData.EventBody);
                    log.LogInformation($"C# Event Hub trigger function processed a message: {eventBody}");
                    dynamic data = JsonConvert.DeserializeObject(eventBody);
                    string deviceId = data.deviceId;
                    FullUser owner = await Database.getSingleUserByDeviceId(client, deviceId, log);
                    // Add a JSON document to the output container.
                await fallEventsDocumentsOut.AddAsync(new
                {
                    // create a random ID
                    id = System.Guid.NewGuid().ToString(),
                    user_id = owner.Id,
                    device_id = deviceId,
                    timestamp = System.DateTime.Now.ToString()
                });
                    string resourceUri = $"https://{NH_NAMESPACE}.servicebus.windows.net/{HUB_NAME}/messages/";
                    using (var request = CreateHttpRequest(HttpMethod.Post, resourceUri, deviceId))
                    {
                        string content = getNotificationString(owner.Name, deviceId);
                        request.Content = new StringContent(content, Encoding.UTF8, "application/json");
                        var httpClient = new HttpClient();
                        var response = await httpClient.SendAsync(request);
                        log.LogInformation(response.StatusCode.ToString());
                    }
                                        
                }
                catch (Exception e)
                {
                    // We need to keep processing the rest of the batch - capture this exception and continue.
                    // Also, consider capturing details of the message that failed processing so it can be processed again later.
                    exceptions.Add(e);
                }
            }

            // Once processing of the batch is complete, if any messages in the batch failed processing throw an exception so that there is a record of the failure.

            if (exceptions.Count > 1)
                throw new AggregateException(exceptions);

            if (exceptions.Count == 1)
                throw exceptions.Single();
        }
        private static HttpRequestMessage CreateHttpRequest(HttpMethod method, String resourceUri, String deviceId)
        {
            var request = new HttpRequestMessage(method, $"{resourceUri}?api-version=2017-04");
            request.Headers.Add("Authorization", "SharedAccessSignature sr=https%3A%2F%2Farc-notification-hub-namespace.servicebus.windows.net%2Farc-notification-hub&sig=GWKH1XbiCn5qFOm6DSk3UHHc%2BHCcORZ53tlGBTJGCcE%3D&se=11661451928&skn=DefaultFullSharedAccessSignature");
            request.Headers.Add("ServiceBusNotification-Tags", deviceId);
            request.Headers.Add("ServiceBusNotification-Format", "gcm");

            return request;
        }
    }
}
