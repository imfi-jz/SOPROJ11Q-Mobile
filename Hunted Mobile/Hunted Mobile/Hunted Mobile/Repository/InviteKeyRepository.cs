﻿using Hunted_Mobile.Model;
using Hunted_Mobile.Service;
using Hunted_Mobile.Service.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Hunted_Mobile.Repository {
    public class InviteKeyRepository {
        public async Task<List<InviteKey>> GetAll(string inviteCode) {
            var response = new HttpClientResponse();
            await response.Convert(HttpClientRequestService.Get($"invite-keys/{inviteCode}"));

            List<InviteKey> result = new List<InviteKey>();

            InviteKey key = new InviteKeyJsonService().ToObject(response.ResponseContent);

            key.Value = inviteCode;
            if(string.IsNullOrEmpty(key.Role)) {
                key.ErrorMessages = response.ErrorMessages.Count() > 0 ? response.ErrorMessages : new Dictionary<string, string>() {
                    { "value", response.Status == HttpStatusCode.NotFound ? "De code is niet gevonden" : "Er is iets misgegaan" }
                };
            }

            result.Add(key);
            return result;
        }

        public async Task<List<InviteKey>> GetAll(int gameId) {
            var response = new HttpClientResponse();
            response.HasMultipleResults = true;

            await response.Convert(HttpClientRequestService.GetAll($"games/{gameId}/invite-keys"));

            List<InviteKey> inviteKeys = new List<InviteKey>();

            foreach(InviteKey inviteKey in new InviteKeyJsonService().ToObjects(response.ResponseContent)) {
                inviteKey.GameId = gameId;
                inviteKeys.Add(inviteKey);
            }

            return inviteKeys;
        }
    }
}
