﻿using ICanPay.Core;
using ICanPay.Core.Exceptions;
using ICanPay.Core.Request;
using ICanPay.Core.Response;
using ICanPay.Core.Utils;
using ICanPay.Wechatpay.Request;
using ICanPay.Wechatpay.Response;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace ICanPay.Wechatpay
{
    internal static class SubmitProcess
    {
        private static string _gatewayUrl;

        internal static TResponse Execute<TModel, TResponse>(Merchant merchant, Request<TModel, TResponse> request, string gatewayUrl = null) where TResponse : IResponse
        {
            AddMerchant(merchant, request, gatewayUrl);

            X509Certificate2 cert = null;
            if (((BaseRequest<TModel, TResponse>)request).IsUseCert)
            {
                //TODO:测试
                cert = new X509Certificate2(merchant.SslCertPath, merchant.SslCertPassword);
            }

            string result = null;
            Task.Run(async () =>
            {
                result = await HttpUtil
                 .PostAsync(request.RequestUrl, request.GatewayData.ToXml(), cert);
            })
            .GetAwaiter()
            .GetResult();

            BaseResponse baseResponse;
            if (!(request is BillDownloadRequest || request is FundFlowDownloadRequest))
            {
                var gatewayData = new GatewayData();
                gatewayData.FromXml(result);

                baseResponse = (BaseResponse)(object)gatewayData.ToObject<TResponse>(StringCase.Snake);
                baseResponse.Raw = result;
                if (baseResponse.ReturnCode == "SUCCESS")
                {
                    string sign = gatewayData.GetStringValue("sign");

                    if (!CheckSign(gatewayData, merchant.Key, sign))
                    {
                        throw new GatewayException("签名验证失败");
                    }

                    baseResponse.Execute(merchant, request);
                }
            }
            else
            {
                baseResponse = (BaseResponse)Activator.CreateInstance(typeof(TResponse));
                baseResponse.Raw = result;
                baseResponse.Execute(merchant, request);
            }

            return (TResponse)(object)baseResponse;
        }

        private static void AddMerchant<TModel, TResponse>(Merchant merchant, Request<TModel, TResponse> request, string gatewayUrl) where TResponse : IResponse
        {
            if (!string.IsNullOrEmpty(gatewayUrl))
            {
                _gatewayUrl = gatewayUrl;
            }

            if (!request.RequestUrl.StartsWith("http"))
            {
                request.RequestUrl = _gatewayUrl + request.RequestUrl;
            }
            request.GatewayData.Add(merchant, StringCase.Snake);
            if (!string.IsNullOrEmpty(request.NotifyUrl))
            {
                request.GatewayData.Add("notify_url", request.NotifyUrl);
            }

            if (!(request is WapPayRequest || request is AppletPayRequest ||
                request is PublicPayRequest || request is AppPayRequest ||
                request is ScanPayRequest))
            {
                request.GatewayData.Remove("notify_url");
            }

            request.GatewayData.Add("sign", BuildSign(request.GatewayData, merchant.Key));
        }

        internal static string BuildSign(GatewayData gatewayData, string key)
        {
            gatewayData.Remove("sign");

            string data = $"{gatewayData.ToUrl(false)}&key={key}";
            return EncryptUtil.MD5(data);
        }

        internal static bool CheckSign(GatewayData gatewayData, string key, string sign)
        {
            return BuildSign(gatewayData, key) == sign;
        }
    }
}
