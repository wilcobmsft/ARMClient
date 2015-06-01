using System;
using System.Linq;
using System.Threading.Tasks;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Contracts;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Collections.Generic;

namespace ARMClient.Library.Runner
{
    class Program
    {
        private static void Main(string[] args)
        {
            Run().Wait();
        }

        private static async Task Run()
        {
            var azureClient = new AzureClient();

            var resrouceGroupsResponse = await azureClient.HttpInvoke(HttpMethod.Get, new Uri("https://management.azure.com/subscriptions/2d41f884-3a5d-4b75-809c-7495edb04a0f/resourceGroups?api-version=2014-04-01"));
            resrouceGroupsResponse.EnsureSuccessStatusCode();

            dynamic resourceGroups = await resrouceGroupsResponse.Content.ReadAsAsync<JObject>();
            foreach (var resourceGroup in resourceGroups.value)
            {
                var sitesResponse = await azureClient.HttpInvoke(HttpMethod.Get, new Uri("https://management.azure.com/subscriptions/2d41f884-3a5d-4b75-809c-7495edb04a0f/resourceGroups/" + resourceGroup.name + "/providers/Microsoft.Web/sites?api-version=2015-02-01"));
                sitesResponse.EnsureSuccessStatusCode();

                var sites = await sitesResponse.Content.ReadAsAsync<ArmArrayWrapper<ArmWrapper<Site>>>();
                Console.Write(sites.value.Aggregate(string.Empty, (ac, e) => string.Concat(ac, e.name, Environment.NewLine)));
            }
        }
    }

    public class ArmArrayWrapper<T>
    {
        public T[] value { get; set; }
    }

    public class ArmWrapper<T>
    {
        public string location { get; set; }
        public string name { get; set; }
        public T properties { get; set; }
    }

    public class Site
    {
    }
}