﻿using Mapsui.UI.Forms;

using System;
using System.Collections.Generic;
using System.Text;

namespace Hunted_Mobile.Model {
    public struct Location {
        public double Lattitude { get; set; }
        public double Longitude { get; set; }

        public Location(double lattitude, double longitude) {
            Lattitude = lattitude;
            Longitude = longitude;
        }

        public static implicit operator Location (Position p) => new Location(p.Latitude, p.Longitude);
    }
}
