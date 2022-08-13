using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Plugin.Misc.CoffeeApp;
using Nop.Plugin.Misc.CoffeeApp.Attributes.Square;
using Nop.Plugin.Misc.CoffeeAppCore.Domain.Square;
using Nop.Plugin.Misc.CoffeeAppCore.Models.Square;
using Nop.Plugin.Payments.Square.Domain;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Square;
using Square.Exceptions;
using Square.Models;
using Environment = Square.Environment;


namespace Nop.Plugin.Misc.CoffeeAppCore.Services.POS
{
    public class SquareService
    {
        private readonly SquareSettingsService _squareSettingService;
        private HttpClient _httpClient;
        private readonly INotificationService _notificationService;
        private SquareSettings _squareSettings;
        private string _baseUrl;
        private readonly ILogger _logger;


        public SquareService(
            INotificationService notificationService,
            SquareSettingsService squareSettingService,
            ILogger logger
        )
        {
            _squareSettingService = squareSettingService;
            _squareSettings = new SquareSettings();
            _notificationService = notificationService;
            _logger = logger;
        }


        public SquareService SwitchSandboxOrProduction(int caCafeId)
        {
            _squareSettings = _squareSettingService.GetSettingsCaCafe(caCafeId);

            if (_squareSettings == null)
            {
                throw new Exception("Missing setup data Sqaure");
            }

            this.InitHttpSqaure();

            _baseUrl = _squareSettings.UseSandbox
                ? DevConstants.SquareHelper.Sandbox
                : DevConstants.SquareHelper.Production;
            _httpClient.BaseAddress = new Uri(_baseUrl);
            return this;
        }

        private void InitHttpSqaure()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(20);
            _httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, MimeTypes.ApplicationJson);
        }

        public SquareClient InstanceClient()
        {
            return new SquareClient.Builder()
                .Environment(_squareSettings.UseSandbox ? Environment.Sandbox : Environment.Production)
                .AccessToken(_squareSettings.AccessToken)
                .AddAdditionalHeader("user-agent", SquarePaymentDefaults.UserAgent)
                .Build();
        }

        public string GenerateAuthorizeUrl(ConfigurationModel model)
        {
            SwitchSandboxOrProduction(Int32.Parse(model.CaCafeId));

            var serviceUrl = $"{_baseUrl}authorize";

            var permissionScopes = new List<string>
            {
                "MERCHANT_PROFILE_READ",
                "PAYMENTS_READ",
                "PAYMENTS_WRITE",
                "CUSTOMERS_READ",
                "CUSTOMERS_WRITE",
                "SETTLEMENTS_READ",
                "BANK_ACCOUNTS_READ",
                "ITEMS_READ",
                "ITEMS_WRITE",
                "ORDERS_READ",
                "ORDERS_WRITE",
                "EMPLOYEES_READ",
                "EMPLOYEES_WRITE",
                "TIMECARDS_READ",
                "TIMECARDS_WRITE"
            };

            //request all of the permissions
            var requestingPermissions = string.Join(" ", permissionScopes);

            //create query parameters for the request
            var queryParameters = new Dictionary<string, string>
            {
                ["client_id"] = model.ApplicationId,
                ["response_type"] = "code",
                ["scope"] = requestingPermissions,
                ["session"] = "false",
                ["state"] = model.CaCafeId,
            };

            return QueryHelpers.AddQueryString(serviceUrl, queryParameters);
        }

        public async Task<SquareSettings> AccessTokenCallback(SquareCallbackQuery squareCallbackQuery)
        {
            //handle access token callback

            try
            {
                SwitchSandboxOrProduction(Int32.Parse(squareCallbackQuery.State));

                _squareSettings = _squareSettingService.GetSettingsCaCafe(Int32.Parse(squareCallbackQuery.State));

                if (_squareSettings == null)
                {
                    throw new NopException("The verification string did not pass the validation");
                }

                //check whether there are errors in the request
                if (!string.IsNullOrEmpty(squareCallbackQuery.Error) |
                    !string.IsNullOrEmpty(squareCallbackQuery.ErrorDescription))
                    throw new NopException($"{squareCallbackQuery.Error} - {squareCallbackQuery.ErrorDescription}");

                //check whether there is an authorization code in the request
                if (string.IsNullOrEmpty(squareCallbackQuery.Code))
                    throw new NopException("No service response");

                //exchange the authorization code for an access token
                var (accessToken, refreshToken) = await ObtainAccessTokenAsync(squareCallbackQuery.Code);

                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
                    throw new NopException("No service response");

                //if access token successfully received, save it for the further usage
                _squareSettings.AccessToken = accessToken;
                _squareSettings.RefreshToken = refreshToken;

                _squareSettingService.Update(_squareSettings);

                return _squareSettings;
            }
            catch (Exception exception)
            {
                //display errors
                _notificationService.ErrorNotification("Error");
                if (!string.IsNullOrEmpty(exception.Message))
                    _notificationService.ErrorNotification(exception.Message);
                return null;
            }
        }

        public async Task<(string AccessToken, string RefreshToken)> ObtainAccessTokenAsync(string authorizationCode)
        {
            try
            {
                //get response
                var request = new ObtainAccessTokenRequest
                {
                    ApplicationId = _squareSettings.ApplicationId,
                    ApplicationSecret = _squareSettings.ApplicationSecret,
                    GrantType = GrantType.New,
                    AuthorizationCode = authorizationCode
                };
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "token");
                httpRequest.Headers.Add(HeaderNames.Authorization, $"Client {_squareSettings.ApplicationSecret}");
                httpRequest.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8,
                    MimeTypes.ApplicationJson);

                var response = await _httpClient.SendAsync(httpRequest);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Error("Square" + response.ReasonPhrase);
                }

                //return received access token
                var responseContent = await response.Content.ReadAsStringAsync();
                var accessTokenResponse = JsonConvert.DeserializeObject<ObtainAccessTokenResponse>(responseContent);
                return (accessTokenResponse?.AccessToken, accessTokenResponse?.RefreshToken);
            }
            catch (AggregateException exception)
            {
                //rethrow actual exception
                throw exception.InnerException;
            }
        }

        public async Task<(string AccessToken, string RefreshToken)> RenewAccessTokenAsync()
        {
            try
            {
                //get response
                var request = new ObtainAccessTokenRequest
                {
                    ApplicationId = _squareSettings.ApplicationId,
                    ApplicationSecret = _squareSettings.ApplicationSecret,
                    GrantType = GrantType.Refresh,
                    RefreshToken = _squareSettings.RefreshToken
                };

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "token");
                httpRequest.Headers.Add(HeaderNames.Authorization, $"Client {_squareSettings.ApplicationSecret}");
                httpRequest.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, MimeTypes.ApplicationJson);

                var response = await _httpClient.SendAsync(httpRequest);

                //return received access token
                var responseContent = await response.Content.ReadAsStringAsync();
                var accessTokenResponse = JsonConvert.DeserializeObject<ObtainAccessTokenResponse>(responseContent);
                return (accessTokenResponse?.AccessToken, accessTokenResponse?.RefreshToken);
            }
            catch (AggregateException exception)
            {
                //rethrow actual exception
                throw exception.InnerException;
            }
        }

        public async Task<bool> RevokeAccessTokensAsync()
        {
            try
            {
                //get response
                var request = new RevokeAccessTokenRequest
                {
                    ApplicationId = _squareSettings.ApplicationId,
                    AccessToken = _squareSettings.AccessToken
                };
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "revoke");
                httpRequest.Headers.Add(HeaderNames.Authorization, $"Client {_squareSettings.ApplicationSecret}");
                httpRequest.Content = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, MimeTypes.ApplicationJson);

                var response = await _httpClient.SendAsync(httpRequest);

                //return result
                var responseContent = await response.Content.ReadAsStringAsync();
                var accessTokenResponse = JsonConvert.DeserializeObject<RevokeAccessTokenResponse>(responseContent);
                return accessTokenResponse?.SuccessfullyRevoked ?? false;
            }
            catch (AggregateException exception)
            {
                //rethrow actual exception
                throw exception.InnerException;
            }
        }

        public async Task<IList<Location>> GetActiveLocationsAsync()
        {
            var client = InstanceClient();

            try
            {
                var listLocationsResponse = client.LocationsApi.ListLocations();
                if (listLocationsResponse == null)
                    throw new NopException("No service response");

                //filter active locations and locations that can process credit cards
                var activeLocations = listLocationsResponse.Locations?.Where(location =>
                    location?.Status == SquarePaymentDefaults.LOCATION_STATUS_ACTIVE
                    && (location.Capabilities?.Contains(SquarePaymentDefaults.LOCATION_CAPABILITIES_PROCESSING) ??
                        false)).ToList();
                if (!activeLocations?.Any() ?? true)
                    throw new NopException("There are no active locations for the account");

                return activeLocations;
            }
            catch (Exception exception)
            {
                await CatchExceptionAsync(exception);

                return new List<Location>();
            }
        }

        private async Task<string> CatchExceptionAsync(Exception exception)
        {
            //log full error
            var errorMessage = exception.Message;

            // check Square exception
            if (exception is ApiException apiException)
            {
                //try to get error details
                if (apiException?.Errors?.Any() ?? false)
                    errorMessage = string.Join(";", apiException.Errors.Select(error => error.Detail));
            }

            return errorMessage;
        }
    }
}