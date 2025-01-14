﻿using Hunted_Mobile.Model.GameModels;
using Hunted_Mobile.Service;
using Hunted_Mobile.Service.Json;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xamarin.Forms;

namespace Hunted_Mobile.Repository {
    public class LootRepository {
        // Get all loot that is linked to a game
        public async Task<List<Loot>> GetAll(int gameId) {
            var response = new HttpClientResponse() {
                HasMultipleResults = true,
            };
            await response.Convert(HttpClientRequestService.Get($"games/{gameId}/loot"));
            
            var result = new List<Loot>(
                new LootJsonService().ToObjects(response.ResponseContent)
            );

            return result;
        }

        public async Task<bool> Delete(int lootId) {
            var response = new HttpClientResponse();

            await response.Convert(HttpClientRequestService.Delete($"loot/{lootId}"));

            return response.IsSuccessful;
        }
    }
}
