﻿using Hunted_Mobile.Model;

using MapsuiPosition = Mapsui.UI.Forms.Position;
using Mapsui.UI.Forms;
using Hunted_Mobile.Model.GameModels;
using Xamarin.Forms;
using System;
using Hunted_Mobile.Model.Resource;
using Hunted_Mobile.Repository;
using Hunted_Mobile.Enum;
using Hunted_Mobile.Model.GameModels.Gadget;
using System.Linq;
using System.Collections.Generic;

namespace Hunted_Mobile.Service.Map {
    public class MapViewService {
        const string LOOT_TAG = "loot",
            THIEF_TAG = PlayerRole.THIEF;

        private readonly Color policePinColor = Xamarin.Forms.Color.FromRgb(39, 96, 203),
            thiefPinColor = Xamarin.Forms.Color.Black;
        private readonly Resource policeBadgeIcon;
        private readonly Resource moneyBagIcon;
        private Player player;
        private Pin playerPin;
        private Circle playerRadius;

        public Player Player { 
            get => player;
            set {
                player = value;
                SetPlayerPin();
                UpdatePlayerPinLocation(player.Location);
            }
        }
        public MapView MapView { get; set; }

        public MapViewService(MapView mapView, Player player) {
            MapView = mapView;
            Player = player;
            SetPlayerPin();

            // Enableling this buttons is something for a different PR
            DisableDefaultMapViewOptions();

            policeBadgeIcon = UnitOfWork.Instance.ResourceRepository.GetMapImage("police-badge.png");
            moneyBagIcon = UnitOfWork.Instance.ResourceRepository.GetMapImage("money-bag.png");
        }

        public void UpdatePlayerPinLocation(Location location) {
            playerPin.Position = new MapsuiPosition(location.Latitude, location.Longitude);
            playerRadius.Center = new MapsuiPosition(location.Latitude, location.Longitude);
            AddPlayerPin();
        }

        public void AddPlayerPin() {
            if(!MapView.Pins.Contains(playerPin)) {
                MapView.Pins.Add(playerPin);
                MapView.Drawables.Add(playerRadius);
            }
        }

        private bool AreSameTeam(Player playerA, Player playerB) {
            return (playerA is Police && playerB is Police)
                || (playerA is Thief && playerB is Thief);
        }

        /// <summary>
        /// This methode will only display the user if the person is in the same team as the player
        /// </summary>
        /// <param name="player"></param>
        public void AddTeamMatePin(Player player) {
            if(Player.Id != player.Id && AreSameTeam(Player, player)) {
                if(player is Thief) {
                    AddThiefPin(player.UserName, player.Location);
                }
                else AddPolicePin(player.UserName, player.Location);
            }
        }

        public void AddThiefPin(string username, Location location) {
            MapView.Pins.Add(new Pin(MapView) {
                Label = username,
                Color = thiefPinColor,
                Position = new MapsuiPosition(location.Latitude, location.Longitude),
                Scale = 0.666f,
                Tag = THIEF_TAG,
                Transparency = 0.25f,
            });
        }

        public void AddPolicePin(string username, Location location) {
            MapView.Pins.Add(new Pin(MapView) {
                Label = username,
                Color = policePinColor,
                Position = new MapsuiPosition(location.Latitude, location.Longitude),
                Scale = 0.666f,
                Transparency = 0.25f,
            });
        }

        public void AddLootPin(Loot loot) {
            MapView.Pins.Add(new Pin(MapView) {
                Label = loot.Name,
                Position = new MapsuiPosition(loot.Location.Latitude, loot.Location.Longitude),
                Scale = 1.0f,
                Tag = LOOT_TAG,
                Icon = moneyBagIcon.Data,
                Type = PinType.Icon,
            });
        }

        public void AddPoliceStationPin(Location policeStationLocation) {
            if(policeStationLocation != null) {
                MapView.Pins.Add(new Pin(MapView) {
                    Label = "Politie station",
                    Position = new MapsuiPosition(policeStationLocation.Latitude, policeStationLocation.Longitude),
                    Scale = 1.0f,
                    Icon = policeBadgeIcon.Data,
                    Type = PinType.Icon,
                });
            }
        }

        private void SetPlayerPin() {
            playerPin = new Pin(MapView) {
                Label = Player.UserName,
                Color = Player is Thief ? thiefPinColor : policePinColor,
            };
            playerRadius = new Circle() {
                FillColor = new Color(255, 0, 0, 0.2),
                Center = new MapsuiPosition(Player.Location.Latitude, Player.Location.Longitude),
                Radius = new Distance(20),
                Quality = 360
            };
        }

        private void DisableDefaultMapViewOptions() {
            MapView.IsZoomButtonVisible = false;
            MapView.IsNorthingButtonVisible = false;
            MapView.IsMyLocationButtonVisible = false;
        }
    }
}
