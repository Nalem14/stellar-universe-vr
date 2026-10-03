using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.Utils;
using Newtonsoft.Json.Linq;

namespace Core.App
{
    /// <summary>One offer of the galactic marketplace (GetMarketplaceData listings[]).</summary>
    public sealed class MarketListing
    {
        public int Id;
        public int UserId;
        public int PlanetId;
        public int SystemId;
        /// <summary>"mineral" (a resource: mineral / crystal / biomass) or "module" (hangar modules).</summary>
        public string Category = string.Empty;
        public string ItemKey = string.Empty;
        /// <summary>Localised by the server (resource name, module name).</summary>
        public string ItemName = string.Empty;
        public float Quantity;
        public string PriceCurrency = "crystal";
        public float PriceAmount;
        public int CargoVolume;
        /// <summary>Hold a buyer's convoy needs: max(goods volume, payment) — sent by the server since web 0417693.</summary>
        public int RequiredCargo;
        public string SellerName = string.Empty;
        public string EmpireName = string.Empty;
        public string PlanetName = string.Empty;
        public float Distance;
        public string Status = string.Empty;

        public bool IsModule => Category == "module";

        public static MarketListing Parse(JToken t) => new()
        {
            Id = FocusContext.AsInt(t["id"]),
            UserId = FocusContext.AsInt(t["userid"]),
            PlanetId = FocusContext.AsInt(t["planetid"]),
            SystemId = FocusContext.AsInt(t["systemid"]),
            Category = FocusContext.AsString(t["category"]),
            ItemKey = FocusContext.AsString(t["item_key"]),
            ItemName = FocusContext.AsString(t["item_name"]),
            Quantity = FocusContext.AsFloat(t["quantity"]),
            PriceCurrency = FocusContext.AsString(t["price_currency"]),
            PriceAmount = FocusContext.AsFloat(t["price_amount"]),
            CargoVolume = FocusContext.AsInt(t["cargo_volume"]),
            RequiredCargo = FocusContext.AsInt(t["required_cargo"]),
            SellerName = FocusContext.AsString(t["seller_name"]),
            EmpireName = FocusContext.AsString(t["empire_name"]),
            PlanetName = FocusContext.AsString(t["planet_name"]),
            Distance = FocusContext.AsFloat(t["distance"]),
            Status = FocusContext.AsString(t["status"])
        };
    }

    public sealed class MarketPage
    {
        public int Total;
        public int Page = 1;
        public int Limit = 6;
        public readonly List<MarketListing> Listings = new();
        public int Pages => System.Math.Max(1, (Total + Limit - 1) / System.Math.Max(1, Limit));
    }

    /// <summary>A ship of ours in orbit that can haul (GetPlanetTradeStatus orbiting_fleets[]).</summary>
    public sealed class MarketHauler
    {
        public int Id;
        public string Name = string.Empty;
        public int ShipsCount;
        public float CargoFree;
        public float CargoTotal;
        public float Speed;
        public bool HasHyperdrive;
        /// <summary>An orbital fortress (StationCore aboard): the server refuses it in a convoy (stationCannotMove).</summary>
        public bool IsStation;
    }

    public sealed class MarketMission
    {
        public int Id;
        public int ListingId;
        public int FleetId;
        /// <summary>outbound (payment aboard, to the seller) / inbound (goods aboard, home).</summary>
        public string Phase = string.Empty;
        public string Status = string.Empty;
        public long DepartureTime;
        public long OutboundArrival;
        public int OriginSystemId;
        public int TargetSystemId;
        public long InboundArrival;
        public string ItemName = string.Empty;
        public float Quantity;
        public string OriginName = string.Empty;
        public string TargetName = string.Empty;
        public string FleetName = string.Empty;
        public bool Bought;
    }

    /// <summary>What one of our worlds can trade (GetPlanetTradeStatus).</summary>
    public sealed class MarketContext
    {
        public int PlanetId;
        public string PlanetName = string.Empty;
        public int SystemId;
        public float Mineral;
        public float Crystal;
        public float Biomass;
        public readonly List<(string type, string name, int count)> Hangar = new();
        public readonly List<MarketHauler> Haulers = new();
        public float OrbitCargoFree;
        public readonly List<MarketListing> MyListings = new();
        public readonly List<MarketMission> Missions = new();

        public float Stock(string resource) => resource switch
        {
            "mineral" => Mineral,
            "crystal" => Crystal,
            "biomass" => Biomass,
            _ => 0f
        };
    }

    /// <summary>
    /// The galactic marketplace (web 57e1074 → 3dfe6c3, model/marketplace.php): offers between empires paid in
    /// resources, bought by a physical freight convoy. Reads only while a market screen is open (the server
    /// also resolves arrived convoys on these reads, ProcessTradeMissions), orders as the web panel sends them.
    /// Failures are ordinary <c>error:</c> answers (translated by the server).
    /// </summary>
    public static class MarketService
    {
        public static readonly string[] Resources = { "mineral", "crystal", "biomass" };

        /// <summary>Native name key of a resource ("mineralResource"…).</summary>
        public static string ResourceKey(string resource) => resource + "Resource";

        public static async Task<(MarketPage page, string error)> Browse(string category, string search, string sort,
            int page, int limit, int currentSystemId)
        {
            var q = new Dictionary<string, string>
            {
                { "category", string.IsNullOrEmpty(category) ? "all" : category },
                { "sort", string.IsNullOrEmpty(sort) ? "recent" : sort },
                { "page", page.ToString(CultureInfo.InvariantCulture) },
                { "limit", limit.ToString(CultureInfo.InvariantCulture) },
                { "currentSystemId", currentSystemId.ToString(CultureInfo.InvariantCulture) }
            };
            if (!string.IsNullOrEmpty(search))
                q["search"] = search;
            var r = await ActionJs.Get("GetMarketplaceData", q);
            if (!r.Ok)
                return (null, r.Error);
            try
            {
                var root = JObject.Parse(r.Body);
                var data = root["listings"] != null ? root : root["data"] as JObject ?? root;
                var p = new MarketPage
                {
                    Total = FocusContext.AsInt(data["total"]),
                    Page = System.Math.Max(1, FocusContext.AsInt(data["page"])),
                    Limit = System.Math.Max(1, FocusContext.AsInt(data["limit"]))
                };
                if (data["listings"] is JArray rows)
                    foreach (var row in rows)
                        p.Listings.Add(MarketListing.Parse(row));
                return (p, null);
            }
            catch
            {
                return (null, Trans.Get("vr.common.error"));
            }
        }

        public static async Task<(MarketContext ctx, string error)> Context(int planetId)
        {
            var q = new Dictionary<string, string>();
            if (planetId > 0)
                q["planet"] = planetId.ToString(CultureInfo.InvariantCulture);
            var r = await ActionJs.Get("GetPlanetTradeStatus", q);
            if (!r.Ok)
                return (null, r.Error);
            try
            {
                var root = JObject.Parse(r.Body);
                var data = root["planet"] != null ? root : root["data"] as JObject ?? root;
                var c = new MarketContext();
                if (data["planet"] is JObject p)
                {
                    c.PlanetId = FocusContext.AsInt(p["id"]);
                    c.PlanetName = FocusContext.AsString(p["name"]);
                    c.SystemId = FocusContext.AsInt(p["systemid"]);
                    c.Mineral = FocusContext.AsFloat(p["mineral"]);
                    c.Crystal = FocusContext.AsFloat(p["crystal"]);
                    c.Biomass = FocusContext.AsFloat(p["biomass"]);
                }

                if (data["hangar_modules"] is JArray hangar)
                    foreach (var h in hangar)
                        c.Hangar.Add((FocusContext.AsString(h["type"]), FocusContext.AsString(h["name"]), FocusContext.AsInt(h["count"])));
                if (data["orbiting_fleets"] is JArray fleets)
                    foreach (var f in fleets)
                        c.Haulers.Add(new MarketHauler
                        {
                            Id = FocusContext.AsInt(f["id"]),
                            Name = FocusContext.AsString(f["name"]),
                            ShipsCount = FocusContext.AsInt(f["ships_count"]),
                            CargoFree = FocusContext.AsFloat(f["cargo_free"]),
                            CargoTotal = FocusContext.AsFloat(f["cargo_total"]),
                            Speed = FocusContext.AsFloat(f["speed"]),
                            HasHyperdrive = FocusContext.AsBool(f["has_hyperdrive"]),
                            IsStation = HasCore(f["ships"], "StationCore")
                        });
                c.OrbitCargoFree = FocusContext.AsFloat(data["total_orbiting_cargo_free"]);
                if (data["my_active_listings"] is JArray mine)
                    foreach (var l in mine)
                        c.MyListings.Add(MarketListing.Parse(l));
                var me = FocusContext.OwnedUserId();
                if (data["active_missions"] is JArray missions)
                    foreach (var m in missions)
                        c.Missions.Add(new MarketMission
                        {
                            Id = FocusContext.AsInt(m["id"]),
                            ListingId = FocusContext.AsInt(m["listing_id"]),
                            FleetId = FocusContext.AsInt(m["fleet_id"]),
                            Phase = FocusContext.AsString(m["phase"]),
                            Status = FocusContext.AsString(m["status"]),
                            DepartureTime = FocusContext.AsLong(m["departure_time"]),
                            OutboundArrival = FocusContext.AsLong(m["outbound_arrival"]),
                            OriginSystemId = FocusContext.AsInt(m["origin_systemid"]),
                            TargetSystemId = FocusContext.AsInt(m["target_systemid"]),
                            InboundArrival = FocusContext.AsLong(m["inbound_arrival"]),
                            ItemName = FocusContext.AsString(m["item_name"]),
                            Quantity = FocusContext.AsFloat(m["quantity"]),
                            OriginName = FocusContext.AsString(m["origin_planet_name"]),
                            TargetName = FocusContext.AsString(m["target_planet_name"]),
                            FleetName = FocusContext.AsString(m["fleet_name"]),
                            Bought = FocusContext.AsInt(m["buyer_userid"]) == me
                        });
                return (c, null);
            }
            catch
            {
                return (null, Trans.Get("vr.common.error"));
            }
        }

        static bool HasCore(JToken ships, string type)
        {
            if (ships is not JArray rows)
                return false;
            foreach (var s in rows)
                if (FocusContext.AsString(s["type"]) == type)
                    return true;
            return false;
        }

        /// <summary>Publish a sale (escrow: the goods leave the planet at once). Returns null or the error.</summary>
        public static async Task<string> Create(int planetId, string category, string itemKey, float quantity, string currency, float price)
        {
            var r = await ActionJs.Get("CreateMarketOffer", new Dictionary<string, string>
            {
                { "planet", planetId.ToString(CultureInfo.InvariantCulture) },
                { "listing_type", "sell" },
                { "category", category },
                { "item_key", itemKey },
                { "quantity", quantity.ToString("0.##", CultureInfo.InvariantCulture) },
                { "price_currency", currency },
                { "price_amount", price.ToString("0.##", CultureInfo.InvariantCulture) }
            });
            return r.Ok ? null : r.Error;
        }

        public static async Task<string> Cancel(int listingId)
        {
            var r = await ActionJs.Get("CancelMarketOffer", new Dictionary<string, string>
            {
                { "listing_id", listingId.ToString(CultureInfo.InvariantCulture) }
            });
            return r.Ok ? null : r.Error;
        }

        /// <summary>Buy: the convoy leaves <paramref name="originPlanet"/> with the payment, comes back with the goods.</summary>
        public static async Task<(bool ok, string error, int ships)> Dispatch(int listingId, int originPlanet, IReadOnlyList<int> fleets)
        {
            var r = await ActionJs.Get("DispatchMarketConvoy", new Dictionary<string, string>
            {
                { "listing_id", listingId.ToString(CultureInfo.InvariantCulture) },
                { "origin_planet", originPlanet.ToString(CultureInfo.InvariantCulture) },
                { "fleet_ids", string.Join(",", fleets) }
            });
            return r.Ok ? (true, null, fleets.Count) : (false, r.Error, 0);
        }
    }
}
