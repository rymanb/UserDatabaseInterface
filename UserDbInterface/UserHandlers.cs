
using UserDBInterface.Azure;
using UserDBInterface.Utils;
using Microsoft.Extensions.Primitives;
using System.Text.Json;
using System.IO;
using System.Text;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.Extensions;

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Core;

namespace UserDBInterface.Service;


// This is the core logic of the web server and hosts all of the HTTP
// handlers used by the web server regarding File Server functionality.
public class Handlers
{
    private readonly IConfiguration _configuration;
    private readonly Logger _logger;
    private readonly CosmosDbWrapper _cosmosDbWrapper;

    public Handlers(IConfiguration configuration)
    {
        _configuration = configuration;
        if (null == _configuration)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        string serviceName = configuration["Logging:ServiceName"];
        _logger = new Logger(serviceName);

        _cosmosDbWrapper = new CosmosDbWrapper(configuration);
    }

    private static string GetParameterFromList(string parameterName, HttpRequest request, MethodLogger log)
    {
        // Obtain the parameter from the caller
        if (request.Query.TryGetValue(parameterName, out StringValues items))
        {
            if (items.Count > 1)
            {
                throw new UserErrorException($"Multiple {parameterName} found");
            }

            log.SetAttribute($"request.{parameterName}", items[0]);
        }
        else
        {
            throw new UserErrorException($"No {parameterName} found");
        }

        return items[0];
    }

    public async Task AddUser(HttpContext context)
    {
        using(var log = _logger.StartMethod(nameof(AddUser), context))
        {
            try
            {
                // SecretClientOptions options = new SecretClientOptions()
                // {
                //     Retry =
                //     {
                //         Delay= TimeSpan.FromSeconds(2),
                //         MaxDelay = TimeSpan.FromSeconds(16),
                //         MaxRetries = 5,
                //         Mode = RetryMode.Exponential
                //     }
                // };

                // var secretClient = new SecretClient(new Uri("https://fileservicekeyvault.vault.azure.net/"), new DefaultAzureCredential(),options);

                // KeyVaultSecret secret = secretClient.GetSecret("UserDBKey");

                // string secretValue = secret.Value;

                // // get header
                // string header = context.Request.Headers["x-auth-key"];

                // if (header != secretValue)
                // {
                //     throw new UserErrorException("Invalid x-auth-key");
                // }


                // Get the user metadata from the request
                string requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
                // makesure body is not empty
                if (string.IsNullOrEmpty(requestBody))
                {
                    throw new UserErrorException("Request body is empty");
                }

                UserMetadata m = JsonSerializer.Deserialize<UserMetadata>(requestBody);

                // Validate the metadata
                Validator.ValidateObject(m, new ValidationContext(m), true);

                

                // Get the metadata from the CosmosDb
                if (await _cosmosDbWrapper.GetItemAsync<UserMetadata>(m.id, m.userid) != null)
                {
                    await _cosmosDbWrapper.UpdateItemAsync(m.id, m.userid, m);
                }
                else
                {
                    await _cosmosDbWrapper.AddItemAsync(m, m.userid);
                }
            }
            catch(Exception e)
            {
                log.HandleException(e);
            }
        }
    }

    public async Task GetUser(HttpContext context)
    {
        using(var log = _logger.StartMethod(nameof(GetUser), context))
        {
            try
            {
                SecretClientOptions options = new SecretClientOptions()
                {
                    Retry =
                    {
                        Delay= TimeSpan.FromSeconds(2),
                        MaxDelay = TimeSpan.FromSeconds(16),
                        MaxRetries = 5,
                        Mode = RetryMode.Exponential
                    }
                };

                var secretClient = new SecretClient(new Uri("https://fileservicekeyvault.vault.azure.net/"), new DefaultAzureCredential(),options);

                KeyVaultSecret secret = secretClient.GetSecret("UserDBKey");

                string secretValue = secret.Value;

                // get header
                string header = context.Request.Headers["x-auth-key"];

                if (header != secretValue)
                {
                    log.LogUserError("Invalid x-auth-key - expected: " + secretValue + " received: " + header);
                    throw new UserErrorException("Invalid x-auth-key");
                }

                

                // Get the user id from the query string
                string userId = GetParameterFromList("userid", context.Request, log);

                // Get the metadata from the CosmosDb
                IEnumerable<UserMetadata> metadata = await GetMetadataFromCosmosDb(userId);

                // Serialize the metadata to JSON
                string json = JsonSerializer.Serialize(metadata);

                // Return the JSON to the caller
                await context.Response.WriteAsync(json);
            }
            catch(Exception e)
            {
                log.HandleException(e);
            }
        }
    }

    // Health Checks (aka ping) methods are handy to have on your service
    // They allow you to report that your are alive and return any other
    // information that is useful. These are often used by load balancers
    // to decide whether to send you traffic. For example, if you need a long
    // time to initialize, you can report that you are not ready yet.
    public async Task HealthCheckDelegate(HttpContext context)
    {
        // "using" is a C# system to ensure that the object is disposed of properly
        // when the block is exited. In this case, it will call the Dispose method
        using(var log = _logger.StartMethod(nameof(HealthCheckDelegate), context))
        {
            try
            {
                // Generally, a 200 OK is returned if the service is alive
                // and that is all that the load balancer needs, but a
                // text message can be useful for humans.
                // However, in some cases, the LB will be able to process more
                // health information to know how to react to your service, so
                // don't be surprised if you see code with more involved health 
                // checks.
                await context.Response.WriteAsync("Alive");
            }
            catch(Exception e)
            {
                // While you can just throw the exception back to the web server,
                // it is not recommended. It is better to catch the exception and
                // log it, then return a 500 Internal Server Error to the caller yourself.
                log.HandleException(e);
            }
        }
    }

    private async Task<IEnumerable<UserMetadata>> GetMetadataFromCosmosDb(string userId)
    {
        string queryString = $"SELECT * FROM c WHERE c.userid = '{userId}'";
        return await _cosmosDbWrapper.GetItemsAsync<UserMetadata>(queryString);
    }
}