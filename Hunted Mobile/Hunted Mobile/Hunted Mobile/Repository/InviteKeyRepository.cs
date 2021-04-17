﻿using Hunted_Mobile.Model;
using Hunted_Mobile.Service;

using System.Threading.Tasks;

namespace Hunted_Mobile.Repository {
    public class InviteKeyRepository {
        public async Task<InviteKey> Get(string inviteCode) {
            var response = new HttpClientResponse();
            await response.Convert(HttpClientRequestService.Get($"invite-key/{inviteCode}"));

            return response.IsSuccessful ? new InviteKey() {
                Value = (string) response.Item.GetValue("value"),
                GameId = (int) response.Item.GetValue("game_id"),
                Role = (string) response.Item.GetValue("role")
            } : null;
        }
    }
}
