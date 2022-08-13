using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Nop.Core;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Square.Exceptions;
using Square.Models;
using NopOrder = Nop.Core.Domain.Orders.Order;

namespace Nop.Plugin.Misc.CoffeeAppCore.Services.POS
{
    public class SquareOrderManager
    {
        #region Ctor

        public SquareOrderManager(
            SquareService squareService,
            IOrderService orderService,
            ICustomerService customerService,
            IProductService productService
        )
        {
            _squareService = squareService;
            _orderService = orderService;
            _customerService = customerService;
            _productService = productService;
        }

        #endregion

        #region Fields

        private readonly SquareService _squareService;
        private readonly IOrderService _orderService;
        private readonly ICustomerService _customerService;
        private readonly IProductService _productService;

        #endregion

        public Order CreateOrder(CreateOrderRequest orderRequest,int cafeId)
        {
            if (orderRequest == null)
                throw new ArgumentNullException(nameof(orderRequest));

            var client = _squareService.SwitchSandboxOrProduction(cafeId).InstanceClient();

            var orderResponse = client.OrdersApi.CreateOrder(orderRequest);

            if (orderResponse == null)
                throw new NopException("No service response");

            return orderResponse.Order;
        }

        public CreateOrderRequest CreateOrderRequestFromOrder(NopOrder order, string locationId)
        {
            if (_orderService.GetOrderItems(order.Id) is var orderItems && orderItems == null)
                throw new ArgumentNullException(nameof(orderItems));

            var customer = _customerService.GetCustomerById(order.CustomerId);

            var bodyOrderLineItems = new List<OrderLineItem>();


            var recipient = new OrderFulfillmentRecipient.Builder()
          .DisplayName(customer.Email)
          .Build();

            var pickupDetails = new OrderFulfillmentPickupDetails.Builder()
          .Recipient(recipient)
          .PickupAt(XmlConvert.ToString(DateTime.Now, "yyyy-MM-ddTHH:mm:sszzzzzzz"))
          .Build();

            var orderFulfillment = new OrderFulfillment.Builder()
          .Type("PICKUP")
          .State(SquarePaymentDefaults.ORDER_STATUS_PROCESED)
          .PickupDetails(pickupDetails)
          .Build();


            var fulfillments = new List<OrderFulfillment>();
            fulfillments.Add(orderFulfillment);

           
            foreach (var orderItem in orderItems)
            {
               var product =  _productService.GetProductById(orderItem.ProductId);

                bodyOrderLineItems.Add(new OrderLineItem.Builder(
                        orderItem.Quantity.ToString())
                    .Name(product.Name)
                    .Note(Regex.Replace(orderItem.AttributeDescription, "<.*?>", string.Empty))
                    .Quantity(orderItem.Quantity.ToString())
                    .BasePriceMoney(new Money.Builder()
                        .Amount(Convert.ToInt64(orderItem.OriginalPrice * 100))
                        .Currency(order.CustomerCurrencyCode)
                        .Build())
                    .Build()
                ) ;
            }

            return new CreateOrderRequest.Builder()
                .Order(new Order.Builder(locationId)
                    .ReferenceId(order.CustomOrderNumber)
                    .LineItems(bodyOrderLineItems)
                    .Fulfillments(fulfillments)
                    .Build())
                .IdempotencyKey(order.OrderGuid.ToString())
                .Build();
        }

        private static Task<string> CatchExceptionAsync(Exception exception)
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

            return Task.FromResult(errorMessage);
        }
    }
}