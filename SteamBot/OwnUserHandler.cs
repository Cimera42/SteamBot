using SteamKit2;
using System.Collections.Generic;
using SteamTrade;
using SteamTrade.TradeWebAPI;
using System;
using Newtonsoft.Json;
using System.Net;
using System.IO;
using System.Timers;
using SteamTrade.TradeOffer;
using System.Text;
using HtmlAgilityPack;
using System.Globalization;
using SteamAuth;

namespace SteamBot
{
    public class OwnUserHandler : UserHandler
	{
		public OwnUserHandler (Bot bot, SteamID sid) : base(bot, sid) {}

		System.Timers.Timer tradeCheckTimer;
		bool stillTrading = false;
		Dictionary<int, bool> tradesDone = new Dictionary<int, bool>();

		public class TradeUser
		{
			public ulong steamid;
			public string token;
		}

		public class TradeData
		{
			public int tradeid;
			public TradeUser user;
			public List<long> items;
			public string type;
			public string code;
			public int status;
			public string steamid;
		}

		public class TradeList
		{
			public List<TradeData> trades;
		}

		private void checkForTrades(object CancellationTokenSource, ElapsedEventArgs e)
		{
			string password = System.IO.File.ReadAllText(@"E:\Programming\Web\cstrade_admin_password.txt");
			if(stillTrading == false)
			{
				stillTrading = true;

				//Log.Info("Checking http://10.0.0.53:4001/php/get_items.php for new trades");

				string getUrl = "http://10.0.0.53:4001/backend/get_items.php";
				var getRequest = (HttpWebRequest)WebRequest.Create (getUrl);

				string getData = "password=" + password;
				getData += "&bot_steam_id=" + Bot.SteamUser.SteamID.ConvertToUInt64();
				var getData_encoded = Encoding.ASCII.GetBytes (getData);

				getRequest.Method = "POST";
				getRequest.ContentType = "application/x-www-form-urlencoded";
				getRequest.ContentLength = getData_encoded.Length;

				using (var stream = getRequest.GetRequestStream ()) {
					stream.Write (getData_encoded, 0, getData_encoded.Length);
				}

				var getResponse = (HttpWebResponse)getRequest.GetResponse ();
				string getString = new StreamReader (getResponse.GetResponseStream()).ReadToEnd();
				TradeList data = JsonConvert.DeserializeObject<TradeList> (getString);

				foreach(TradeData trade in data.trades)
				{
					bool tradeValue;
					if(tradesDone.TryGetValue(trade.tradeid, out tradeValue))
					{
						//Log.Info("Trade " + trade.tradeid + " already made");
					}
					else if(trade.status == 2 || trade.status == 9 || trade.status == 11)
					{
						Log.Info("Incomplete trade missing from update list, adding now " + trade.tradeid);
						tradesDone[trade.tradeid] = true;
						tradeStatuses[trade.tradeid] = new TradeStatus(trade.user.steamid, trade.steamid, trade.tradeid, trade.type);						
					}
					else
					{
						Log.Info("Trade " + trade.tradeid + " not completed. Attempting now.");
						TradeUser user = trade.user;

						var tradeOffer = Bot.NewTradeOffer(new SteamID(user.steamid));
						int count = 0;
						foreach(long item in trade.items)
						{
							Log.Info("adding item " + item + " to trade");
							if(trade.type.Equals("d", StringComparison.Ordinal) || 
							   trade.type.Equals("t", StringComparison.Ordinal))
							{
								tradeOffer.Items.AddTheirItem(730, 2, item);
							}
							else if(trade.type.Equals("w", StringComparison.Ordinal))
							{
								tradeOffer.Items.AddMyItem(730, 2, item);
							}
							count++;
						}
						if(trade.type.Equals("d", StringComparison.Ordinal) || 
						   trade.type.Equals("t", StringComparison.Ordinal))
						{
							Log.Info(count + " items being traded from user " + user.steamid);
						}
						else if(trade.type.Equals("w", StringComparison.Ordinal))
						{
							Log.Info(count + " items being traded to user " + user.steamid);
						}

						if (tradeOffer.Items.NewVersion)
						{
							Log.Info("Current trade version - " + trade.user.token);
							string tradeID;
							try
							{
								if(tradeOffer.SendWithToken(out tradeID, trade.user.token, "Secure Code: " + trade.code))
								{
									Log.Success("New trade offer with id " + tradeID);
									tradesDone[trade.tradeid] = true;
									tradeStatuses[trade.tradeid] = new TradeStatus(user.steamid, tradeID, trade.tradeid, trade.type);

									string setTradeIDData = "password=" + password;
									setTradeIDData += "&trade_id=" + trade.tradeid;
									setTradeIDData += "&trade_steam_id=" + tradeID;

									string url = "http://10.0.0.53:4001/backend/update_trade.php";
									var updaterequest = (HttpWebRequest)WebRequest.Create (url);

									var setTradeID_data = Encoding.ASCII.GetBytes (setTradeIDData);

									updaterequest.Method = "POST";
									updaterequest.ContentType = "application/x-www-form-urlencoded";
									updaterequest.ContentLength = setTradeID_data.Length;

									using (var stream = updaterequest.GetRequestStream ()) {
										stream.Write (setTradeID_data, 0, setTradeID_data.Length);
									}
									var response = (HttpWebResponse)updaterequest.GetResponse ();
									var responseString = new StreamReader (response.GetResponseStream ()).ReadToEnd ();
									if(responseString.Contains("success"))
									{
										Log.Success("Steam trade id set");
									}
								}
								else
								{
									Log.Info("Trade offer failed to send");
								}
							}
							catch (Exception exc)
							{
								Log.Error("Trade offer could not be sent");

								string postData = "password=" + password;
								postData += "&trade_id=" + trade.tradeid;
								postData += "&trade_status=" + 12;
								postData += "&user_steam_id=" + user.steamid;
								postData += "&bot_steam_id=" + Bot.SteamUser.SteamID.ConvertToUInt64();

								string url = "http://10.0.0.53:4001/backend/update_trade.php";
								var updaterequest = (HttpWebRequest)WebRequest.Create (url);

								var failedTrade_data = Encoding.ASCII.GetBytes (postData);

								updaterequest.Method = "POST";
								updaterequest.ContentType = "application/x-www-form-urlencoded";
								updaterequest.ContentLength = failedTrade_data.Length;

								using (var stream = updaterequest.GetRequestStream ()) {
									stream.Write (failedTrade_data, 0, failedTrade_data.Length);
								}
								var response = (HttpWebResponse)updaterequest.GetResponse ();
								var responseString = new StreamReader (response.GetResponseStream ()).ReadToEnd ();
								if(responseString.Contains("success"))
								{
									Log.Success("Trade status updated");
								}
							}
						}
						else
						{
							Log.Info("Old version of trade. Canceling.");
						}
					}
				}
				stillTrading = false;
			}
			//https://steamcommunity.com/tradeoffer/new/?partner=81920318&token=od9DZwUG
		}
			
		Dictionary<int, TradeStatus> tradeStatuses = new Dictionary<int, TradeStatus>();

		public class TradeStatus
		{
			public ulong steam_user_id;
			public string steam_trade_id;
			public int server_trade_id;
			public int status_id;
			public bool itemsPushed;
			public string trade_type;
			public TradeOfferState state;

			public TradeStatus(ulong in_steam_user_id, string in_steam_trade_id, int in_server_trade_id, string in_trade_type)
			{
				steam_user_id = in_steam_user_id;
				steam_trade_id = in_steam_trade_id;
				server_trade_id = in_server_trade_id;
				status_id = 0;
				trade_type = in_trade_type;
				itemsPushed = false;
			}
		}

		public void checkTradeStatuses(object CancellationTokenSource, ElapsedEventArgs e)
		{
			string password = System.IO.File.ReadAllText(@"E:\Programming\Web\cstrade_admin_password.txt");
			foreach(KeyValuePair<int, TradeStatus> trade in tradeStatuses)
			{
				if(trade.Value.state == TradeOfferState.TradeOfferStateCanceled
					|| trade.Value.state == TradeOfferState.TradeOfferStateCountered
					|| trade.Value.state == TradeOfferState.TradeOfferStateDeclined
					|| trade.Value.state == TradeOfferState.TradeOfferStateExpired
					|| trade.Value.state == TradeOfferState.TradeOfferStateInvalid
					|| trade.Value.state == TradeOfferState.TradeOfferStateInvalidItems
					|| trade.Value.state == TradeOfferState.TradeOfferStateCanceledBySecondFactor
					|| trade.Value.itemsPushed == true)
				{
				}
				else
				{
					TradeOffer tradeOfferData;
					bool traderequest = Bot.TryGetTradeOffer(trade.Value.steam_trade_id, out tradeOfferData);
					if(traderequest)
					{
						if(trade.Value.state != tradeOfferData.OfferState)
						{
							Log.Info("Trade " + trade.Value.steam_trade_id + "/" + trade.Value.server_trade_id + " has status of " + tradeOfferData.OfferState + "/" + (int)tradeOfferData.OfferState);
							trade.Value.state = tradeOfferData.OfferState;
						}

						List<long> itemids = new List<long>();
						if(trade.Value.trade_type.Equals("d", StringComparison.Ordinal) ||
						   trade.Value.trade_type.Equals("t", StringComparison.Ordinal) )
						{
							if(tradeOfferData.OfferState == TradeOfferState.TradeOfferStateAccepted)
							{
								string tradeHistory = Bot.SteamWeb.Fetch("http://steamcommunity.com/profiles/76561198258413368/inventoryhistory/", "GET", null, false);
								HtmlDocument doc = new HtmlDocument();
								doc.LoadHtml(tradeHistory);

								HtmlNodeCollection trades = doc.DocumentNode.SelectNodes(".//div[contains(@class, 'tradehistoryrow')]");
								HtmlNode tradeNode = HtmlNode.CreateNode("");
								bool found = false;
								foreach(HtmlNode nextTrade in trades)
								{
									HtmlNode date = nextTrade.SelectSingleNode(".//div[contains(@class, 'tradehistory_date')]");
									HtmlNode time = nextTrade.SelectSingleNode(".//span[contains(@class, 'tradehistory_timestamp')]");

									DateTime thenDate = DateTime.Parse(time.InnerHtml + " " + date.InnerHtml);
									DateTime other = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(thenDate, "Pacific Standard Time", TimeZoneInfo.Utc.Id);
									Log.Info("Yr: {0}, Mon: {1}, Day: {2}, Hr: {3}, Min: {4}", other.Year, other.Month, other.Day, other.Hour, other.Minute);
									var epoch = (other - new DateTime(1970, 1, 1)).TotalSeconds;

									if(Math.Abs(epoch - tradeOfferData.TimeUpdated) < 120)
									{
										tradeNode = nextTrade;
										found = true;
										break;
									}
								}
								if(found)
								{
									HtmlNode receiveditemsNode = tradeNode.SelectSingleNode(".//div[contains(@class, 'tradehistory_items_received')]");
									HtmlNodeCollection receiveditemsList = receiveditemsNode.SelectNodes(".//a[contains(@class, 'history_item')]");
									foreach(HtmlNode item in receiveditemsList)
									{
										string itemdata = item.GetAttributeValue("href", "nope");
										string[] splited = itemdata.Split('_');
										long id = Convert.ToInt64(splited[splited.Length-1]);
										itemids.Add(id);
									}
								}
								else
								{
									Log.Error("Could not find trade " + trade.Value.steam_trade_id);
								}
							}
						}
						else if(trade.Value.trade_type.Equals("w", StringComparison.Ordinal))
						{
							if(tradeOfferData.OfferState == TradeOfferState.TradeOfferStateNeedsConfirmation)
							{
								Bot.AcceptAllMobileTradeConfirmations();
							}
						}

						string postData = "password=" + password;
						postData += "&trade_id=" + trade.Value.server_trade_id;
						postData += "&steam_trade_id=" + trade.Value.steam_trade_id;
						postData += "&trade_status=" + (int)tradeOfferData.OfferState;
						postData += "&user_steam_id=" + trade.Value.steam_user_id;
						postData += "&bot_steam_id=" + Bot.SteamUser.SteamID.ConvertToUInt64();
						postData += "&trade_asset_ids=" + JsonConvert.SerializeObject(itemids);
						postData += "&trade_type=" + trade.Value.trade_type;

						string url = "http://10.0.0.53:4001/backend/update_trade.php";
						var updaterequest = (HttpWebRequest)WebRequest.Create (url);

						var data = Encoding.ASCII.GetBytes (postData);

						updaterequest.Method = "POST";
						updaterequest.ContentType = "application/x-www-form-urlencoded";
						updaterequest.ContentLength = data.Length;

						using (var stream = updaterequest.GetRequestStream ()) {
							stream.Write (data, 0, data.Length);
						}
						var response = (HttpWebResponse)updaterequest.GetResponse ();
						var responseString = new StreamReader (response.GetResponseStream ()).ReadToEnd ();
						if(responseString.Contains("success"))
						{
							Log.Success("Trade " + trade.Value.steam_trade_id + "/" + trade.Value.server_trade_id + " status updated");
						}
						if(trade.Value.trade_type.Equals("d", StringComparison.Ordinal))
						{
							if(responseString.Contains("itemspushed"))
							{
								trade.Value.itemsPushed = true;
								Log.Success("Trade items successfully added to user account");
							}
						}
						else if(responseString.Contains("completed"))
						{
							trade.Value.itemsPushed = true;
						}
					}
					else
					{
						Log.Info("Trade offer state request failed");
					}
				}
			}
		}

        public override void OnLoginCompleted()
        {
			tradeCheckTimer = new System.Timers.Timer ();
			tradeCheckTimer.Elapsed += new ElapsedEventHandler (checkForTrades);
			tradeCheckTimer.Elapsed += new ElapsedEventHandler (checkTradeStatuses);
			tradeCheckTimer.Interval = 5000;
			tradeCheckTimer.Start ();
        }

		public override void OnBotCommand(string command)
		{
			if(command.Equals("genAuthCode", StringComparison.Ordinal))
			{
				Log.Success(Bot.SteamGuardAccount.GenerateSteamGuardCode());
			}
		}

		public override void OnNewTradeOffer(TradeOffer offer)
		{
			if(IsAdmin)
			{
				Log.Info("New Tradeoffer from admin! Accepting and confirming.");
				offer.Accept();
				Bot.AcceptAllMobileTradeConfirmations();
			}
			else
			{
				string password = System.IO.File.ReadAllText(@"E:\Programming\Web\cstrade_admin_password.txt");
				string postData = "password=" + password;
				postData += "&other_steam_id=" + OtherSID.ConvertToUInt64();

				string url = "http://10.0.0.53:4001/backend/check_bot.php";
				var updaterequest = (HttpWebRequest)WebRequest.Create (url);

				var data = Encoding.ASCII.GetBytes (postData);

				updaterequest.Method = "POST";
				updaterequest.ContentType = "application/x-www-form-urlencoded";
				updaterequest.ContentLength = data.Length;

				using (var stream = updaterequest.GetRequestStream ()) {
					stream.Write (data, 0, data.Length);
				}
				var response = (HttpWebResponse)updaterequest.GetResponse ();
				var responseString = new StreamReader (response.GetResponseStream ()).ReadToEnd ();
				if(responseString.Contains("success"))
				{
					Log.Success("Confirming trade from fellow bot");
					offer.Accept();
					Bot.AcceptAllMobileTradeConfirmations();
				}
				else
				{
					offer.Decline();
				}
			}
		}
			
		public override bool OnGroupAdd() {	return false;}
		public override bool OnFriendAdd () { return false;}
		public override void OnFriendRemove () {}
		public override void OnMessage (string message, EChatEntryType type) {}

        public override bool OnTradeRequest() 
        {
			Log.Success ("Trade request made");
            return false;
        }

        public override void OnTradeError (string error) 
        {
            SendChatMessage("Oh, there was an error: {0}.", error);
            Log.Warn (error);
        }
			
        public override void OnTradeTimeout () 
        {
            SendChatMessage("Sorry, but you were AFK and the trade was canceled.");
            Log.Info ("User was kicked because he was AFK.");
        }

        public override void OnTradeInit() 
        {
			SendTradeMessage("Success. Please put up your items.");
        }

        public override void OnTradeAddItem (Schema.Item schemaItem, Inventory.Item inventoryItem) 
		{
			Log.Info ("Item Added");
			Log.Info ("Appid: {0}", inventoryItem.AppId);
			Log.Info ("Id: {0}", inventoryItem.Id);
		}

        public override void OnTradeRemoveItem (Schema.Item schemaItem, Inventory.Item inventoryItem) 
		{
			Log.Info ("Item Removed");
			Log.Info ("Appid: {0}", inventoryItem.AppId);
			Log.Info ("Id: {0}", inventoryItem.Id);
		}

        public override void OnTradeMessage (string message) 
		{
			Log.Info ("Received trade message: {0}", message);
		}

        public override void OnTradeReady (bool ready) 
        {
            if (!ready)
            {
            }
            else
            {
                if(IsAdmin)
                {
                    Trade.SetReady (true);
                }
            }
        }

        public override void OnTradeSuccess()
        {
            Log.Success("Trade Complete.");
        }

		public override void OnTradeAwaitingConfirmation(long tradeOfferID)
        {
            Log.Warn("Trade ended awaiting confirmation");
            SendChatMessage("Please complete the confirmation to finish the trade");
        }

        public override void OnTradeAccept() 
        {
            if (IsAdmin)
            {
                //Even if it is successful, AcceptTrade can fail on
                //trades with a lot of items so we use a try-catch
                try {
                    if (Trade.AcceptTrade())
                        Log.Success("Trade Accepted!");
                }
                catch {
                    Log.Warn ("The trade might have failed, but we can't be sure.");
                }
            }
        }
    }
 
}

