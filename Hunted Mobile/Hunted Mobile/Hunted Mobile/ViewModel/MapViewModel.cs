﻿using Hunted_Mobile.Model;
using Hunted_Mobile.Model.GameModels;
using Hunted_Mobile.Repository;

using Mapsui;
using Mapsui.Geometries;
using Mapsui.Projection;
using Mapsui.UI;
using Mapsui.Layers;
using Mapsui.Providers;
using Mapsui.Styles;
using Mapsui.UI.Forms;
using Mapsui.Utilities;
using Mapsui.Widgets;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hunted_Mobile.Service;
using Hunted_Mobile.Service.Gps;
using System.Timers;

namespace Hunted_Mobile.ViewModel {
    public class MapViewModel {
        private MapView _mapView;
        private readonly Model.Map _mapModel;
        private readonly Game _gameModel;
        private readonly LootRepository _lootRepository;
        private readonly UserRepository _userRepository;
        private readonly GpsService _gpsService;
        private readonly WebSocketService _socketService;
        private Timer _intervalUpdateTimer;
        private Pin _playerPin;

        public MapViewModel(Game gameModel, Model.Map mapModel) {
            _mapModel = mapModel;
            _gameModel = gameModel;
            _gpsService = new GpsService();
            _lootRepository = new LootRepository();
            _socketService = new WebSocketService(gameModel.Id);
            _userRepository = new UserRepository();

            if(!_gpsService.GpsHasStarted()) {
                _gpsService.StartGps();
            }
            _gpsService.LocationChanged += MyLocationUpdated;

            if(!WebSocketService.Connected) {
                _socketService.Connect();
            }
            _socketService.ResumeGame += StartIntervalTimer;
            _socketService.PauseGame += StopIntervalTimer;
            _socketService.EndGame += StopIntervalTimer;
            _socketService.IntervalEvent += DisplayOtherPins;
            StartIntervalTimer();
        }

        public void SetMapView(MapView mapView) {
            _mapView = mapView;

            AddOsmLayerToMapView();
            AddGameBoundary();
            LimitViewportToGame();
        }

        private void StopIntervalTimer() {
            if(_intervalUpdateTimer != null) {
                _intervalUpdateTimer.Stop();
                _intervalUpdateTimer.Dispose();
                _intervalUpdateTimer = null;
            }
        }

        private void StartIntervalTimer() {
            StopIntervalTimer();
            _intervalUpdateTimer = new Timer((_gameModel.Interval - 5) * 1000);
            _intervalUpdateTimer.AutoReset = false;
            _intervalUpdateTimer.Elapsed += IntervalUpdate;
            _intervalUpdateTimer.Start();
        }

        private async void IntervalUpdate(object sender, ElapsedEventArgs args) {
            StopIntervalTimer();
            _intervalUpdateTimer = new Timer(_gameModel.Interval * 1000);
            _intervalUpdateTimer.AutoReset = false;
            _intervalUpdateTimer.Elapsed += IntervalUpdate;
            _intervalUpdateTimer.Start();
            // Send the current user's location to the database
            await _userRepository.Update(_mapModel.PlayingUser.Id, _mapModel.PlayingUser.Location);
            // Get loot update from the database
            await UpdateLoot(_gameModel.Id);
        }

        /// <summary>
        /// Action to execute when the device location has updated
        /// </summary>
        private void MyLocationUpdated(Location newLocation) {
            _mapModel.PlayingUser.Location = newLocation;

            // Send update to the map view
            Mapsui.UI.Forms.Position mapsuiPosition = new Mapsui.UI.Forms.Position(newLocation.Latitude, newLocation.Longitude);
            _mapView.MyLocationLayer.UpdateMyLocation(mapsuiPosition, true);

            DisplayPlayerPin();
        }

        private void CenterMapOnLocation(Location center, double zoomResolution) {
            Point centerPoint = new Mapsui.UI.Forms.Position(center.Latitude, center.Longitude).ToMapsui();
            _mapView.Navigator.CenterOn(centerPoint);

            _mapView.Navigator.NavigateTo(centerPoint, zoomResolution);
        }

        /// <summary>
        /// Ensures the map panning is limited to given number around a given center location
        /// </summary>
        private void LimitMapViewport(Location center, int limit = 100000) {
            _mapView.Map.Limiter = new ViewportLimiterKeepWithin();
            Point centerPoint = new Mapsui.UI.Forms.Position(center.Latitude, center.Longitude).ToMapsui();
            Point min = new Point(centerPoint.X - limit, centerPoint.Y - limit);
            Point max = new Point(centerPoint.X + limit, centerPoint.Y + limit);
            _mapView.Map.Limiter.PanLimits = new BoundingBox(min, max);
        }

        /// <summary>
        /// Ensures the map panning is limited to the game's boundary
        /// </summary>
        private void LimitViewportToGame() {
            Location center = _mapModel.GameBoundary.GetCenter();
            double diameter = _mapModel.GameBoundary.GetDiameter();
            int viewPortSizeMultiplier = 70000;
            LimitMapViewport(center, (int) (diameter * viewPortSizeMultiplier));

            BoundingBox gameArea = new BoundingBox(new List<Geometry>() { _mapModel.GameBoundary.ToPolygon() });

            while(!_mapView.Map.Limiter.PanLimits.Contains(gameArea)) {
                viewPortSizeMultiplier += 5000;
                LimitMapViewport(center, (int) (diameter * viewPortSizeMultiplier));
            }

            CenterMapOnLocation(center, diameter * 175);
        }

        private void ZoomMap(double resolution) {
            _mapView.Navigator.ZoomTo(resolution);
        }

        /// <summary>
        /// Adds the required OpenStreetMap layer to the mapView
        /// </summary>
        private void AddOsmLayerToMapView() {
            var map = new Mapsui.Map {
                CRS = "EPSG:3857",
                Transformation = new MinimalTransformation()
            };

            map.Layers.Add(OpenStreetMap.CreateTileLayer());
            map.Widgets.Add(new Mapsui.Widgets.ScaleBar.ScaleBarWidget(map) { TextAlignment = Alignment.Center, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom });

            _mapView.Map = map;
            _mapView.MyLocationLayer.Enabled = false;
        }

        /// <summary>
        /// Adds the visual game boundary as a polygon
        /// </summary>
        private void AddGameBoundary() {
            Boundary boundary = new Boundary();
            boundary.Points.Add(new Location(51.779043, 5.506003));
            boundary.Points.Add(new Location(51.761559, 5.491387));
            boundary.Points.Add(new Location(51.743866, 5.506616));
            boundary.Points.Add(new Location(51.755662, 5.553818));
            boundary.Points.Add(new Location(51.772993, 5.546168));

            _mapModel.GameBoundary = boundary;

            _mapView.Map.Layers.Add(CreateBoundaryLayer());
        }

        /// <summary>
        /// Creates a layer to display the game boundary
        /// </summary>
        private ILayer CreateBoundaryLayer() {
            MemoryProvider memoryProvider = new MemoryProvider(_mapModel.GameBoundary.ToPolygon());
            return new Layer("Polygon") {
                DataSource = memoryProvider,
                Style = new VectorStyle {
                    Fill = new Brush(new Color(0, 0, 0, 0)),
                    Outline = new Pen {
                        Color = Color.Red,
                        Width = 2,
                        PenStyle = PenStyle.DashDotDot,
                        PenStrokeCap = PenStrokeCap.Round
                    }
                }
            };
        }

        private void DisplayPlayerPin() {
            if(_playerPin == null) {
                _playerPin = new Pin(_mapView) {
                    Label = _mapModel.PlayingUser.Name ?? "",
                    Color = Xamarin.Forms.Color.FromRgb(39, 96, 203)
                };
            }

            _playerPin.Position = new Mapsui.UI.Forms.Position(_mapModel.PlayingUser.Location.Latitude, _mapModel.PlayingUser.Location.Longitude);

            if(!_mapView.Pins.Contains(_playerPin)) {
                _mapView.Pins.Add(_playerPin);
            }
        }

        /// <summary>
        /// Displays pins for all game objects with a location
        /// </summary>
        private void DisplayOtherPins() {
            _mapView.Pins.Clear();

            // TODO: the Name property of users is null here, it should not be
            // Players
            foreach(var user in _mapModel.GetUsers()) {
                _mapView.Pins.Add(new Pin(_mapView) {
                    Label = user.Name ?? "",
                    Color = Xamarin.Forms.Color.Black,
                    Position = new Mapsui.UI.Forms.Position(user.Location.Latitude, user.Location.Longitude),
                    Scale = 0.666f,
                });
            }

            // Loot
            foreach(var loot in _mapModel.GetLoot()) {
                _mapView.Pins.Add(new Pin(_mapView) {
                    Label = loot.Name,
                    Color = Xamarin.Forms.Color.Gold,
                    Position = new Mapsui.UI.Forms.Position(loot.Location.Latitude, loot.Location.Longitude),
                    Scale = 0.5f,
                    // TODO change icon of loot
                });
            }
        }

        /// <summary>
        /// Gets all the loot from the database and updates the _model
        /// </summary>
        private async Task UpdateLoot(int gameId) {
            var lootList = await _lootRepository.GetAll(gameId);

            _mapModel.SetLoot(lootList);
        }
    }
}
