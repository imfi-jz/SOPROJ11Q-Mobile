﻿using Hunted_Mobile.Model;
using Hunted_Mobile.Model.GameModels;
using Hunted_Mobile.Repository;

using MapsuiPosition = Mapsui.UI.Forms.Position;
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
using System.Windows.Input;
using Xamarin.Forms;
using System.Timers;
using Hunted_Mobile.Model.Resource;
using Hunted_Mobile.View;
using System.Linq;
using Hunted_Mobile.Model.Response;
using Hunted_Mobile.Service.Map;
using Hunted_Mobile.Enum;
using Hunted_Mobile.Service.Preference;

namespace Hunted_Mobile.ViewModel {
    public class MapViewModel : BaseViewModel {
        private const int LOOT_PICKUP_TIME_IN_SECONDES = 5,
            ARREST_THIEF_TIME_IN_SECONDES = 5,
            LOOT_PICKUP_MAX_DISTANCE_IN_METERS = 10,
            POLICE_ARREST_DISTANCE_IN_METERS = 10,
            PICK_UP_LOOT_SCORE = 1,
            ARREST_THIEF_SCORE = 1;

        const string LOOT_TAG = "loot",
            THIEF_TAG = PlayerRole.THIEF;

        private readonly Model.Map mapModel;
        private GpsService gpsService;
        private readonly WebSocketService webSocketService;
        private Loot selectedLoot = new Loot();
        private Game gameModel;
        private readonly View.Messages messagesView;
        private PlayersOverviewPage playersOverview;
        private Timer intervalUpdateTimer;
        private Timer holdingButtonTimer;
        private Thief selectedThief;
        private bool openMainMapMenu = false;
        private bool mainMapMenuButtonVisible = true;
        private readonly Countdown countdown;
        private MapViewService mapViewService;
        private readonly DateTime dateTimeNow;
        private string logoImage;
#pragma warning disable IDE1006 // Naming Styles
        private MapView mapView {
            get => mapViewService?.MapView;
            set {
                mapViewService.MapView = value;
            }
        }
#pragma warning restore IDE1006 // Naming Styles

        public string CounterDisplay => countdown.RemainTime.ToString(@"hh\:mm\:ss");
        public MapDialog MapDialog { get; private set; } = new MapDialog();
        public MapIconsService Icons { get; } = new MapIconsService();

        public MapDialogOptions MapDialogOption {
            get => MapDialog.SelectedDialog;
            set {
                MapDialog.SelectedDialog = value;
                ToggleEnableStatusOnMapView();
            }
        }
        public string LogoImage {
            get => logoImage;
            set {
                logoImage = value;
                OnPropertyChanged("LogoImage");
            }
        }

        public Game GameModel {
            get => gameModel;
            set {
                gameModel = value;
                OnPropertyChanged("GameModel");
            }
        }

        public bool OpenMainMapMenu {
            get => openMainMapMenu;
            set {
                mainMapMenuButtonVisible = !value;
                openMainMapMenu = value;
                OnPropertyChanged("MainMapMenuButtonVisible");
                OnPropertyChanged("OpenMainMapMenu");
            }
        }

        public bool MainMapMenuButtonVisible => mainMapMenuButtonVisible;

        public Loot SelectedLoot {
            get => selectedLoot;
            set {
                selectedLoot = value;
                OnPropertyChanged("SelectedLoot");
            }
        }

        public Thief SelectedThief {
            get => selectedThief;
            set {
                selectedThief = value;
                OnPropertyChanged("SelectedThief");
            }
        }

        public bool IsCloseToSelectedLoot {
            get {
                if(mapModel != null && mapModel.PlayingUser != null && mapModel.PlayingUser.Location != null && SelectedLoot != null) {
                    return mapModel.PlayingUser.Location.DistanceToOtherInMeters(SelectedLoot.Location) <= LOOT_PICKUP_MAX_DISTANCE_IN_METERS;
                }
                return false;
            }
        }

        public bool IsCloseToSelectedThief {
            get {
                if(mapModel != null && mapModel.PlayingUser != null && mapModel.PlayingUser.Location != null && SelectedThief != null) {
                    return mapModel.PlayingUser.Location.DistanceToOtherInMeters(SelectedThief.Location) <= POLICE_ARREST_DISTANCE_IN_METERS;
                }
                return false;
            }
        }

        public bool Initialized { get; private set; }

        public int PlayingUserScore {
            get {
                if(mapModel != null && gameModel != null) {
                    return mapModel.PlayingUser is Thief ? gameModel.ThievesScore : gameModel.PoliceScore;
                }
                return 0;
            }
        }

        public string PlayingUserScoreDisplay => "Score: " + PlayingUserScore;

        public MapViewModel(Game gameModel, Model.Map mapModel) {
            this.mapModel = mapModel;
            this.gameModel = gameModel;
            var gameIdStr = gameModel.Id.ToString();
            var messageViewModel = new MessageViewModel(gameIdStr);
            messagesView = new View.Messages(messageViewModel);
            webSocketService = new WebSocketService(gameIdStr);
            playersOverview = new View.PlayersOverviewPage(new PlayersOverviewViewModel(new List<Player>() { mapModel.PlayingUser }, webSocketService));
            countdown = new Countdown();
            dateTimeNow = DateTime.Now;
            Task.Run(async () => await UpdatePlayerLocation());
            BeforeStartCountdown();
            StartCountdown(0);
            SetGameLogo();

            if(gameModel.Status == GameStatus.PAUSED) {
                PauseGame(null);
            }
            if(gameModel.Status == GameStatus.FINISHED) {
                EndGame(null);
            }
        }

        private void HandlePinClickedCommand(object sender, PinClickedEventArgs args) {
            string tag = $"{args.Pin.Tag}";

            if(tag == LOOT_TAG && mapModel.PlayingUser is Thief) {
                OnLootClicked(args.Pin.Position);
            }
            else if(tag == THIEF_TAG && mapModel.PlayingUser is Police) {
                OnThiefClicked(args.Pin.Position);
            }
        }

        public ICommand NavigateToPlayersOverviewCommand => new Xamarin.Forms.Command((e) => NavigateToPlayersOverview());

        public ICommand NavigateToMessagePageCommand => new Command((e) => NavigateToMessagePage());


        public ICommand ReleasingMapDialogActionButtonCommand => new Xamarin.Forms.Command((e) => {
            if(holdingButtonTimer != null) {
                holdingButtonTimer.Stop();
                holdingButtonTimer = null;
            }
        });

        public ICommand HoldingMapDialogActionButtonCommand => new Xamarin.Forms.Command((e) => {
            holdingButtonTimer = new Timer();

            // Interval is set with milisecondes
            if(MapDialogOption == MapDialogOptions.DISPLAY_PICKUP_LOOT) {
                holdingButtonTimer.Interval = LOOT_PICKUP_TIME_IN_SECONDES * 1000;
                holdingButtonTimer.Elapsed += SuccessfullyPickedUpLoot;
            }
            else if(MapDialogOption == MapDialogOptions.DISPLAY_ARREST_THIEF) {
                holdingButtonTimer.Interval = ARREST_THIEF_TIME_IN_SECONDES * 1000;
                holdingButtonTimer.Elapsed += SuccessfullyArrestThief;
            }

            holdingButtonTimer.Start();
        });

        public ICommand PressedMapDialogActionButtonCommand => new Xamarin.Forms.Command((e) => {
            if(MapDialogOption == MapDialogOptions.DISPLAY_END_GAME) {
                ExitGame();
            }

            MapDialogOption = MapDialogOptions.NONE;
        });

        public ICommand CloseMapDialogCommand => new Xamarin.Forms.Command((e) => {
            MapDialogOption = MapDialogOptions.NONE;
        });

        public ICommand OpenMainMapMenuCommand => new Xamarin.Forms.Command((e) => {
            OpenMainMapMenu = true;
        });

        public ICommand CloseMainMapMenuCommand => new Xamarin.Forms.Command((e) => {
            OpenMainMapMenu = false;
        });

        private async void PreIntervalUpdate(object sender = null, ElapsedEventArgs args = null) {
            StopIntervalTimer();

            // Send the current user's location to the database
            await UpdatePlayerLocation();
        }

        private void SuccessfullyPickedUpLoot(object sender, EventArgs e) {
            holdingButtonTimer.Stop();
            holdingButtonTimer = null;
            MapDialogOption = MapDialogOptions.DISPLAY_PICKUP_LOOT_SUCCESFULLY;
            MapDialog.DisplayPickedUpLootSuccessfully(SelectedLoot.Name);

            Task.Run(async () => {
                // User should be a thief here since a police can't open the dialog
                bool deleted = await UnitOfWork.Instance.LootRepository.Delete(SelectedLoot.Id);
                if(deleted) {
                    await UnitOfWork.Instance.GameRepository.UpdateThievesScore(gameModel.Id, PICK_UP_LOOT_SCORE);
                    await PollLoot();
                    DisplayAllPins();
                }
            });
        }

        private void SuccessfullyArrestThief(object sender, EventArgs e) {
            holdingButtonTimer.Stop();
            holdingButtonTimer = null;
            MapDialogOption = MapDialogOptions.DISPLAY_ARREST_THIEF_SUCCESFULLY;
            MapDialog.DisplayArrestedThiefSuccessfully(SelectedThief.UserName);

            Task.Run(async () => {
                bool isCaught = await UnitOfWork.Instance.UserRepository.CatchThief(SelectedThief.Id);
                if(isCaught) {
                    await UnitOfWork.Instance.GameRepository.UpdatePoliceScore(gameModel.Id, ARREST_THIEF_SCORE);
                }
            });
        }


        private async Task PollLoot() {
            var lootList = await UnitOfWork.Instance.LootRepository.GetAll(gameModel.Id);
            mapModel.Loot = lootList;
        }

        private async Task PollUsers() {
            var userList = new List<Player>();
            foreach(Player user in await UnitOfWork.Instance.UserRepository.GetAll(gameModel.Id)) {
                if(user.Id != mapModel.PlayingUser.Id) {
                    userList.Add(user);
                }
            }
            mapModel.Players = userList;

            playersOverview = new PlayersOverviewPage(
                new PlayersOverviewViewModel(
                    new List<Player>(userList) { mapModel.PlayingUser },
                    webSocketService
                )
            );
        }

        public void StartCountdown(double timeLeft) {
            if(countdown.InitialTimerStart) {
                countdown.EndDate = gameModel.EndTime;
                countdown.InitialTimerStart = false;
            }
            else {
                countdown.EndDate = DateTime.Now.AddSeconds(timeLeft);
            }

            countdown.Start();
            countdown.Ticked += OnCountdownTicked;
            countdown.Completed += OnCountdownCompleted;
        }

        public void StopCountdown() {
            countdown.Ticked -= OnCountdownTicked;
            countdown.Completed -= OnCountdownCompleted;
        }

        void OnCountdownTicked() {
            OnPropertyChanged(nameof(CounterDisplay));
        }

        void OnCountdownCompleted() {
            countdown.RemainTime = new TimeSpan(0, 0, 0);
            OnCountdownTicked();
        }

        private void SetGameLogo() {
            LogoImage = UnitOfWork.Instance.GameRepository.GetLogoUrl(gameModel.Id);

            OnPropertyChanged(nameof(LogoImage));
        }

        private void ScoreUpdated(ScoreUpdatedEventData data) {
            gameModel.ThievesScore = data.ThiefScore;
            gameModel.PoliceScore = data.PoliceScore;

            OnPropertyChanged(nameof(PlayingUserScore));
            OnPropertyChanged(nameof(PlayingUserScoreDisplay));
        }

        private void IntervalOfGame(IntervalEventData data) {
            StartIntervalTimer();

            Location playingUserLocation = mapModel.PlayingUser.Location;
            var newPlayer = new List<Player>();

            foreach(Player player in data.Players) {
                var gadgets = mapModel.Players.Where(p => p.Id == player.Id).FirstOrDefault()?.Gadgets;
                player.Gadgets = gadgets;

                newPlayer.Add(player);

                if(player.Id == mapModel.PlayingUser.Id) {
                    mapModel.PlayingUser = player;
                }
            }
            mapModel.PlayingUser.Location = playingUserLocation;

            mapModel.Players = newPlayer;
            mapModel.Loot = data.Loot;

            DisplayAllPins();
        }

        public void SetMapView(MapView mapView) {
            bool initializedBefore = this.mapView != null;
            mapViewService = new MapViewService(mapView, mapModel.PlayingUser);

            if(!initializedBefore) {
                InitializeMap();
            }
        }

        private void RemovePreviousNavigation() {
            var navigation = Application.Current.MainPage.Navigation;
            while(navigation.NavigationStack.Count > 1) {
                navigation.RemovePage(navigation.NavigationStack.First());
            }
        }

        private void InitializeMap() {
            AddOsmLayerToMapView();

            Task.Run(async () => {
                await AddGameBoundary();
                LimitViewportToGame();

                gpsService = new GpsService(mapModel.GameBoundary.GetCenter(), mapModel.GameBoundary.GetDiameter());
                if(!gpsService.GpsHasStarted()) {
                    await gpsService.StartGps();
                }

                gpsService.LocationChanged += MyLocationUpdated;
                mapView.PinClicked += HandlePinClickedCommand;

                await StartSocket();
                StartIntervalTimer();
                RemovePreviousNavigation();
            });
        }

        private void BeforeStartCountdown() {
            double diffInSecondes = (gameModel.StartTime - dateTimeNow).TotalSeconds;
            gameModel.EndTime.AddSeconds(-diffInSecondes);
        }

        private void StopIntervalTimer() {
            if(intervalUpdateTimer != null) {
                intervalUpdateTimer.Stop();
                intervalUpdateTimer.Dispose();
                intervalUpdateTimer = null;
            }
        }

        private void StartIntervalTimer(float secondsBeforeGameInterval = 5) {
            StopIntervalTimer();
            intervalUpdateTimer = new Timer((gameModel.Interval - secondsBeforeGameInterval) * 1000);
            intervalUpdateTimer.Elapsed += PreIntervalUpdate;
            intervalUpdateTimer.Start();
        }

        private async Task StartSocket() {
            try {
                if(!WebSocketService.Online) {
                    await webSocketService.Connect();
                }

                webSocketService.ResumeGame += ResumeGame;
                webSocketService.PauseGame += PauseGame;
                webSocketService.EndGame += EndGame;
                webSocketService.ThiefCaught += ThiefStatusChanged;
                webSocketService.ThiefReleased += ThiefStatusChanged;
                webSocketService.IntervalEvent += IntervalOfGame;
                webSocketService.ScoreUpdated += ScoreUpdated;
                webSocketService.GadgetsUpdated += GadgetsUpdated;
                webSocketService.ThiefFakePoliceToggle += ThiefFakePoliceToggle;
            }
            catch(Exception e) {
                DependencyService.Get<Toast>().Show("(#1) Er was een probleem met het initialiseren van de web socket (MapViewModel)");
                UnitOfWork.Instance.ErrorRepository.Create(e);
            }
        }

        private void ThiefFakePoliceToggle(PlayerEventData data) {
            Player updatingPlayer = mapModel.Players.Where(player => player.Id == data.Player.Id).FirstOrDefault();

            if(updatingPlayer != null) {
                mapModel.Players.Remove(updatingPlayer);
                mapModel.Players.Add(updatingPlayer);
            }

            if(data.Player.Id == mapModel.PlayingUser.Id) {
                mapModel.PlayingUser = data.Player;
            }
        }

        private void GadgetsUpdated(GadgetsUpdatedEventData data) {
            Player updatingPlayer = mapModel.Players.Where(player => player.Id == data.Player.Id).FirstOrDefault();

            if(updatingPlayer != null) {
                updatingPlayer.Gadgets = data.Gadgets;
            }
        }

        private void EndGame(EventData data) {
            MapDialogOption = MapDialogOptions.DISPLAY_END_GAME;
            MapDialog.DisplayEndScreen();
            StopCountdown();

            StopIntervalTimer();
        }

        private void PauseGame(EventData data) {
            MapDialogOption = MapDialogOptions.DISPLAY_PAUSE;
            MapDialog.DisplayPauseScreen();
            StopCountdown();

            StopIntervalTimer();
        }

        private void ResumeGame(EventData data) {
            MapDialogOption = MapDialogOptions.NONE;
            StartCountdown(data.TimeLeft);
            StartIntervalTimer();

            // Check if user is still in boundaries
            HandlePlayerBoundaries(WasWithinBoundary(), IsWithinBoundary(mapModel.PlayingUser.Location));
        }

        private void ThiefStatusChanged(PlayerEventData data) {
            Task.Run(async () => {
                await PollUsers();
                DisplayAllPins();
            });
        }

        private bool WasWithinBoundary() {
            // Unset coordinates are considered within bounds to prevent incorrect notifications
            return !mapModel.PlayingUser.Location.IsSet() || mapModel.GameBoundary.Contains(mapModel.PlayingUser.Location);
        }

        private bool IsWithinBoundary(Location newLocation) {
            return mapModel.GameBoundary.Contains(newLocation);
        }

        /// <summary>
        /// Action to execute when the device location has updated
        /// </summary>
        private async void MyLocationUpdated(Location newLocation) {
            bool wasWithinBoundary = WasWithinBoundary();
            bool isWithinBoundary = IsWithinBoundary(newLocation);
            mapModel.PlayingUser.Location = newLocation;

            // Send update to the map view
            MapsuiPosition position = new MapsuiPosition(newLocation.Latitude, newLocation.Longitude);
            mapView.MyLocationLayer.UpdateMyLocation(position, true);

            mapViewService.UpdatePlayerPinLocation(mapModel.PlayingUser.Location);
            OnPropertyChanged(nameof(IsCloseToSelectedLoot));

            if(!Initialized) {
                Initialized = true;
                await UpdatePlayerLocation();
            }

            HandlePlayerBoundaries(wasWithinBoundary, isWithinBoundary);
        }

        private async Task UpdatePlayerLocation() {
            await UnitOfWork.Instance.UserRepository.Update(mapModel.PlayingUser.Id, mapModel.PlayingUser.Location);
        }

        private async void HandlePlayerBoundaries(bool wasWithinBoundary, bool isWithinBoundary) {
            var overwritableScreens = new MapDialogOptions[] {
                MapDialogOptions.NONE,
                MapDialogOptions.DISPLAY_ARREST_THIEF_SUCCESFULLY,
                MapDialogOptions.DISPLAY_PICKUP_LOOT_SUCCESFULLY,
            };

            // Only display the boundary screen if there is no other screen visible
            if(overwritableScreens.Contains(MapDialogOption) && !isWithinBoundary) {
                MapDialogOption = MapDialogOptions.DISPLAY_BOUNDARY_SCREEN;
                MapDialog.DisplayBoundaryScreen();
            }
            else if(isWithinBoundary && MapDialogOption == MapDialogOptions.DISPLAY_BOUNDARY_SCREEN) {
                MapDialogOption = MapDialogOptions.NONE;
            }

            if(isWithinBoundary && !wasWithinBoundary) {
                await PostNotificationAboutPlayer(mapModel.PlayingUser.UserName + " bevindt zich weer binnen de spelgrenzen.");
            }
            else if(!isWithinBoundary && wasWithinBoundary) {
                await PostNotificationAboutPlayer(mapModel.PlayingUser.UserName + " heeft de spelgrenzen verlaten!");
            }
        }

        private void CenterMapOnLocation(Location center, double zoomResolution) {
            Mapsui.Geometries.Point centerPoint = new MapsuiPosition(center.Latitude, center.Longitude).ToMapsui();
            mapView.Navigator.CenterOn(centerPoint);
            mapView.Navigator.NavigateTo(centerPoint, zoomResolution);
        }

        /// <summary>
        /// Ensures the map panning is limited to given number around a given center location
        /// </summary>
        private void LimitMapViewport(Location center, int limit = 100000) {
            mapView.Map.Limiter = new ViewportLimiterKeepWithin();
            Mapsui.Geometries.Point centerPoint = new MapsuiPosition(center.Latitude, center.Longitude).ToMapsui();
            Mapsui.Geometries.Point min = new Mapsui.Geometries.Point(centerPoint.X - limit, centerPoint.Y - limit);
            Mapsui.Geometries.Point max = new Mapsui.Geometries.Point(centerPoint.X + limit, centerPoint.Y + limit);
            mapView.Map.Limiter.PanLimits = new BoundingBox(min, max);
            mapView.Map.Limiter.ZoomLimits = new MinMax(0, limit * 0.002);
        }

        /// <summary>
        /// Ensures the map panning is limited to the game's boundary
        /// </summary>
        private void LimitViewportToGame() {
            Location center = mapModel.GameBoundary.GetCenter();
            double diameter = mapModel.GameBoundary.GetDiameter();
            int viewPortSizeMultiplier = 70000;
            LimitMapViewport(center, (int) (diameter * viewPortSizeMultiplier));

            BoundingBox gameArea = new BoundingBox(new List<Geometry>() { mapModel.GameBoundary.ToPolygon() });

            while(!mapView.Map.Limiter.PanLimits.Contains(gameArea)) {
                viewPortSizeMultiplier += 5000;
                LimitMapViewport(center, (int) (diameter * viewPortSizeMultiplier));
            }

            CenterMapOnLocation(center, diameter * 175);
        }

        private void ZoomMap(double resolution) {
            mapView.Navigator.ZoomTo(resolution);
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

            mapView.Map = map;
            mapView.MyLocationLayer.Enabled = false;
        }

        /// <summary>
        /// Adds the visual game boundary as a polygon
        /// </summary>
        private async Task AddGameBoundary() {
            Boundary boundary = await UnitOfWork.Instance.BorderMarkerRepository.GetBoundary(gameModel.Id);

            mapModel.GameBoundary = boundary;
            mapView.Map.Layers.Add(CreateBoundaryLayer());
        }

        /// <summary>
        /// Creates a layer to display the game boundary
        /// </summary>
        private ILayer CreateBoundaryLayer() {
            MemoryProvider memoryProvider = new MemoryProvider(mapModel.GameBoundary.ToPolygon());
            return new Layer("Polygon") {
                DataSource = memoryProvider,
                Style = new VectorStyle {
                    Fill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(0, 0, 0, 0)),
                    Outline = new Pen {
                        Color = Mapsui.Styles.Color.Red,
                        Width = 2,
                        PenStyle = PenStyle.DashDotDot,
                        PenStrokeCap = PenStrokeCap.Round
                    }
                }
            };
        }

        private void NavigateToPlayersOverview() {
            Xamarin.Forms.Application.Current.MainPage.Navigation.PushAsync(playersOverview);
        }

        private void NavigateToMessagePage() {
            Xamarin.Forms.Application.Current.MainPage.Navigation.PushAsync(messagesView);
        }

        /// <summary>
        /// Displays pins for all game objects with a location
        /// </summary>
        private void DisplayAllPins() {
            mapView.Pins.Clear();
            mapViewService.AddPlayerPin();
            mapViewService.AddPoliceStationPin(gameModel.PoliceStationLocation);

            foreach(var thief in mapModel.Thiefs) {
                mapViewService.AddTeamMatePin(thief);
            }

            foreach(var police in mapModel.Police) {
                mapViewService.AddTeamMatePin(police);
            }

            // If current user has role as Police
            if(mapModel.PlayingUser is Police && mapModel.Thiefs.Count > 0) {
                mapViewService.AddClosestThiefPin(GetClosestThief());
            }

            if(mapModel.PlayingUser is Thief) {
                foreach(var loot in mapModel.Loot) {
                    mapViewService.AddLootPin(loot);
                }

                if(mapModel.PlayingUser is FakePolice) {
                    foreach(var police in mapModel.Police) {
                        mapViewService.AddPolicePin(police.UserName, police.Location);
                    }
                }
            }
        }

        private Player GetClosestThief() {
            Player closestThief = null;

            foreach(var thief in mapModel.Thiefs) {
                if(closestThief == null) {
                    closestThief = thief;
                }
                else if(mapModel.PlayingUser.Location.DistanceToOtherInMeters(thief.Location) < mapModel.PlayingUser.Location.DistanceToOtherInMeters(closestThief.Location)) {
                    closestThief = thief;
                }
            }

            return closestThief;
        }

        private void OnLootClicked(Position position) {
            SelectedLoot = mapModel.FindLoot(new Location(position));
            MapDialogOption = MapDialogOptions.DISPLAY_PICKUP_LOOT;
            MapDialog.DisplayPickingUpLoot(SelectedLoot.Name, IsCloseToSelectedLoot);
        }

        private void OnThiefClicked(Position position) {
            SelectedThief = mapModel.FindThief(new Location(position));
            MapDialogOption = MapDialogOptions.DISPLAY_ARREST_THIEF;
            MapDialog.DisplayArrestingThief(SelectedThief.UserName, IsCloseToSelectedThief);
        }

        private void ToggleEnableStatusOnMapView() {
            mapView.Content.IsEnabled = MapDialogOption == MapDialogOptions.NONE;
        }

        private void ExitGame() {
            GameSessionPreference.ClearUserAndGame();
            Xamarin.Forms.Application.Current.MainPage.Navigation.PushAsync(new MainPage());
            RemovePreviousNavigation();
            webSocketService.Disconnect();
        }

        private async Task<bool> PostNotificationAboutPlayer(string message) {
            return await UnitOfWork.Instance.NotificationRepository.Create(
                message,
                gameModel.Id,
                mapModel.PlayingUser.Id
            );
        }
    }
}
