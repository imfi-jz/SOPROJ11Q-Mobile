﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Hunted_Mobile.Service {
    public class HttpClientRequestService {
        public static async Task<HttpResponseMessage> Get(string path) {
            return await GetHttpClient().GetAsync(GetUrl(path));
        }

        // This methode is exact like the Get request, but it can be extended with pagination, filtering and sorting
        public static async Task<HttpResponseMessage> GetAll(string path) {
            return await GetHttpClient().GetAsync(GetUrl(path));
        }

        public static async Task<HttpResponseMessage> Create(string path, object parameters) {
            return await GetHttpClient().PostAsync(GetUrl(path), GetEncodedParameters(parameters));
        }

        public static async Task<HttpResponseMessage> Put(string path, object parameters = null) {
            return await GetHttpClient().PutAsync(GetUrl(path), GetEncodedParameters(parameters));
        }

        public static async Task<HttpResponseMessage> Patch(string path, object parameters = null) {
            var requestMessage = new HttpRequestMessage(new HttpMethod("PATCH"), GetUrl(path));

            if(parameters != null) {
                requestMessage.Content = GetEncodedParameters(parameters);
            }
            
            return await GetHttpClient().SendAsync(requestMessage);
        }

        public static async Task<HttpResponseMessage> Delete(string path) {
            return await GetHttpClient().DeleteAsync(GetUrl(path));
        }

        /// <summary>
        /// Always prepare url with the IPAdress and api path
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string GetUrl(string path) {
            return $"{AppSettings.WebAddress}/api/{path}";
        }

        /// <summary>
        /// Always returns a HttpClient with the DefaultHeaders "application/json"
        /// </summary>
        /// <returns></returns>
        protected static HttpClient GetHttpClient() {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return httpClient;
        }

        /// <summary>
        /// This methode get the properties of a class and place all in FormUrlEncodedContent
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        protected static FormUrlEncodedContent GetEncodedParameters(object parameters) {
            var content = new List<KeyValuePair<string, string>>();

            // Place all properties and values inside a list with KeyValuePairs
            if(parameters != null) {
                foreach(var property in parameters.GetType().GetProperties()) {
                    content.Add(new KeyValuePair<string, string>(property.Name, property.GetValue(parameters).ToString()));
                }
            }

            return new FormUrlEncodedContent(content);
        }
    }
}
