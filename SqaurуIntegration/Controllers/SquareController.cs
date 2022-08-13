using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Description;
using System.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.CoffeeApp.Models;
using Nop.Plugin.Misc.CoffeeAppCore.Models.Square;
using Nop.Plugin.Misc.CoffeeAppCore.Services;
using Nop.Plugin.Misc.CoffeeAppCore.Services.POS;
using Square.Models;

namespace Nop.Plugin.Misc.CoffeeApp.Controllers
{
    public class SquareController : Controller
    {
        private readonly SquareSettingsService _squareSettingService;
        private readonly SquareService _squareService;
        private readonly SquareOrderManager _squareOrderManager;
        private readonly SquarePaymentManager _squarePaymentManager;


        public SquareController(
            SquareSettingsService squareSettingService,
            SquareService squareService,
            SquareOrderManager squareOrderManager,
            SquarePaymentManager squarePaymentManager
        )
        {
            _squareSettingService = squareSettingService;
            _squareService = squareService;
            _squareOrderManager = squareOrderManager;
            _squarePaymentManager = squarePaymentManager;
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("admin/[controller]/configuration")]
        [HttpPost]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            var caCafeSettings = GetSetting(Int32.Parse(model.CaCafeId));

            var CaCafeId = model.CaCafeId;

            if (string.IsNullOrEmpty(caCafeSettings?.AccessToken))
            {
                ModelState.AddModelError(nameof(model.AccessToken), "Access Token is empty.");

            }

            if (string.IsNullOrEmpty(model.LocationId))
            {
                ModelState.AddModelError(nameof(model.LocationId), "Cafe Square location is empty.");
            }

            if (ModelState.IsValid)
            {
                var settings = GetSetting(Int32.Parse(model.CaCafeId));
                settings.LocationId = model.LocationId;

                _squareSettingService.Update(settings);
                model.MessageToken = "Token Success";
                _notificationService.SuccessNotification("Square Connect");
            }

            model.caCafe = ListCompany(Int32.Parse(CaCafeId));
            model.ListSquareConnects = GetListSquareConnect();

            return View($"~/Plugins/Misc.CoffeeApp/Views/Square/Configure.cshtml", model);
        }

        [AuthorizeAdmin]
        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("admin/[controller]/configuration")]
        [HttpPost, ActionName("Configure")]
        [FormValueRequired("obtainAccessToken")]
        [HttpPost]
        public IActionResult ObtainAccessToken(ConfigurationModel model)
        {
            var caCafeSettings = GetSetting(Int32.Parse(model.CaCafeId));

            _squareSettings = new SquareSettings
            {
                ApplicationId = model.ApplicationId,
                UseSandbox = model.UseSandbox,
                ApplicationSecret = model.ApplicationSecret,
                CaCafeId = Int32.Parse(model.CaCafeId)
            };

            if (caCafeSettings == null)
            {
                _squareSettingService.Insert(_squareSettings);
            }
            else
            {
                _squareSettings.Id = caCafeSettings.Id;
                _squareSettingService.Update(_squareSettings);
            }

            return Redirect(_squareService.GenerateAuthorizeUrl(model));
        }


        [Route("admin/[controller]/callbacksquare", Name = "SquareCallback")]
        [HttpGet]
        public async Task<IActionResult> Callback([FromQuery] SquareCallbackQuery squareCallbackQuery)
        {
            ConfigurationModel configurationModel = new ConfigurationModel();
            var result = await _squareService.AccessTokenCallback(squareCallbackQuery);

            if (result != null)
            {
                configurationModel.ApplicationId = result.ApplicationId;
                configurationModel.ApplicationSecret = result.ApplicationSecret;
                configurationModel.CaCafeId = result.CaCafeId.ToString();
                configurationModel.MessageToken = "Token Success";
                configurationModel.UseSandbox = result.UseSandbox;
                _notificationService.SuccessNotification(
                    "Token received successfully. Please, provide Square location.");
            }
            else
            {
                var caCafeSettings = GetSetting(Int32.Parse(squareCallbackQuery.State));
                if (caCafeSettings != null)
                {
                    _squareSettingService.Delete(caCafeSettings);
                }
            }

            configurationModel.caCafe = ListCompany(Int32.Parse(squareCallbackQuery.State));
            configurationModel.ListSquareConnects = GetListSquareConnect();


            return View($"~/Plugins/Misc.CoffeeApp/Views/Square/Configure.cshtml", configurationModel);
        }

        [AuthorizeAdmin]
        [Route("admin/[controller]/configuration")]
        [HttpGet]
        public IActionResult Configure()
        {
            var model = new ConfigurationModel();
            model.caCafe = ListCompany();
            model.ListSquareConnects = GetListSquareConnect();
            return View($"~/Plugins/Misc.CoffeeApp/Views/Square/Configure.cshtml", model);
        }


        private IEnumerable<SelectListItem> ListCompany(int selectedValue = 0)
        {
            var groupCafe = _caCafeRepository.Table
                .Where(_ => !_.IsTemplate && !_.IsArchived && !_.Deleted)
                .Join(_companyRepository.Table, _ => _.CaCompanyId, __ => __.Id,
                    (caCafe, caCompany) => new { caCafe, caCompany })
                .Select(_ => new SelectListItem
                {
                    Text = _.caCafe.Name,
                    Value = _.caCafe.Id.ToString(),
                    Group = (new SelectListGroup { Name = _.caCompany.Name })
                }).ToList();

            var selected = new SelectList(groupCafe, "Value", "Text", selectedValue, "Group.Name");

            foreach (var item in selected)
            {
                if (item.Value == selectedValue.ToString())
                {
                    item.Selected = true;
                    break;
                }
            }

            return selected;
        }

        private SquareSettings GetSetting(int caCAfeId)
        {
            return _squareSettingService.GetSettingsCaCafe(caCAfeId);
        }

        private IList<ListSquareConnect> GetListSquareConnect()
        {
            return _squareSettingService.Table.Join(_caCafeRepository.Table, _ => _.CaCafeId, __ => __.Id,
                    (squareSettings, caCafe) => new { squareSettings, caCafe })
                .Where(_ => !_.caCafe.IsTemplate && !_.caCafe.IsArchived && !_.caCafe.Deleted)
                .Join(_companyRepository.Table, _ => _.caCafe.CaCompanyId, __ => __.Id,
                    (t, caCompany) => new { t.squareSettings, t.caCafe, caCompany })
                .Select(_ => new ListSquareConnect
                {
                    CafeName = _.caCafe.Name,
                    Id = _.caCafe.Id,
                    CompanyName = _.caCompany.Name,
                    UseSandbox = _.squareSettings.UseSandbox,
                    Location = _.squareSettings.LocationId,
                }).ToList();
        }

        [AuthorizeAdmin]
        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("admin/[controller]/configuration")]
        [HttpDelete]
        public object DeleteSquareConnection(int id)
        {
            if (GetSetting(id) is var settings && settings is null)
            {
                throw new Exception("This Cafe doesn't have Square integration");
            }

            _squareSettingService.Delete(settings);

            return new { Result = true };
        }


        [AuthorizeAdmin]
        [Route("admin/[controller]/SendSquare")]
        [HttpPost]
        public object SendSquare(int orderId)
        {
            if (_caOrderAdditionalDataService.GetByOrderId(orderId) is var caOrderAdditionalData &&
                caOrderAdditionalData == null)
            {
                return new { Result = false, Message = DevConstants.Localization.OrderNotFound };
            }

            if (GetSetting(caOrderAdditionalData.CaCafeId) is var squareSettingService && squareSettingService is null)
            {
                return new { Result = false, Message = "This Cafe doesn't have Square integration" };
            }

            if (_orderService.GetOrderById(caOrderAdditionalData.OrderId) is var order &&
                order.PaymentStatus != PaymentStatus.Paid)
            {
                return new { Result = false, Message = "Order must have 'Paid' payment status to send to Square" };
            }

        
            try
            {
                Order squareOrder = _squareOrderManager.CreateOrder(
                    _squareOrderManager.CreateOrderRequestFromOrder(order, squareSettingService.LocationId), squareSettingService.CaCafeId);

                var result = _squarePaymentManager.CreatePaymentAsync(
                           _squarePaymentManager.CreatePaymenrRequest(squareOrder, order, squareSettingService.LocationId)
                          , squareSettingService.CaCafeId);

                return new { Result = true, Message = $"Square order successfully created (Square Id = {squareOrder.Id}) SquarePayment Id = {result.Id}" };
            }
            catch (Exception e)
            {
                Console.WriteLine(e);

                return new { Result = false, Message = e.Message};
            }

          
        }
    }
}