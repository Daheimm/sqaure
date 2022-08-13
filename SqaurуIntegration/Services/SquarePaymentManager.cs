using Nop.Core;
using Square.Exceptions;
using Square.Models;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NopOrder = Nop.Core.Domain.Orders.Order;

namespace Nop.Plugin.Misc.CoffeeAppCore.Services.POS
{
    public class SquarePaymentManager
    {
        private readonly SquareService _squareService;

        public SquarePaymentManager(
            SquareService squareService
            )
        {
            _squareService = squareService;
        }

        public async Task<(Payment, string)> CreatePaymentAsync(CreatePaymentRequest paymentRequest, int cafeId)
        {
            if (paymentRequest == null)
                throw new ArgumentNullException(nameof(paymentRequest));

            var client = _squareService.SwitchSandboxOrProduction(cafeId).InstanceClient();

            try
            {
                var paymentResponse = client.PaymentsApi.CreatePayment(paymentRequest);
                var body = new CompletePaymentRequest.Builder()
                .Build();

                if (paymentResponse == null)
                    throw new NopException("No service response");

                return (paymentResponse.Payment, null);
            }
            catch (Exception exception)
            {
                return (null, await CatchExceptionAsync(exception));
            }
        }

        public CreatePaymentRequest CreatePaymenrRequest(Order order, NopOrder nopOrder, string locationId)
        {
            var amountMoney = new Money.Builder()
              .Amount(order.TotalMoney.Amount)
              .Currency("USD")
              .Build();

            var externalDetails = new ExternalPaymentDetails.Builder(type: "EXTERNAL", source: "STRIPE")
            .Build();

            return new CreatePaymentRequest.Builder(
                sourceId: "EXTERNAL",
                 idempotencyKey: nopOrder.OrderGuid.ToString(),
                 amountMoney: amountMoney)
                .OrderId(order.Id)
                .LocationId(locationId)
                 .ExternalDetails(externalDetails)
                 .Build();
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
