using SteamKit2;
using System.Collections.Generic;
using SteamTrade;
using SteamTrade.TradeWebAPI;
using System;
using Newtonsoft.Json;
using System.Net;
using System.IO;
using SteamTrade.TradeOffer;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using HtmlAgilityPack;
using System.Globalization;
using SteamAuth;

namespace SteamBot
{
    public class OwnUserHandler : UserHandler
	{
		public OwnUserHandler (Bot bot, SteamID sid) : base(bot, sid) {}

		System.Threading.Timer tradeCheckTimer;
		bool debug_output = false;
		Dictionary<int, bool> tradesDone = new Dictionary<int, bool>();

		public class TradeAttempts
		{
			public int attempts = 0;
			public ulong lastAttempt = 0;//(ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
		}

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
			public List<TradeData> trades = new List<TradeData>();
		}

		private void checkForTrades()
		{
			string password = System.IO.File.ReadAllText(@"../cstrade_admin_password.txt");

			TradeList data;
			string getUrl = "http://skinbonanza.com/backend/get_items.php";
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

			for(int attempts = 0;;attempts++)
			{
				try
				{
					var getResponse = (HttpWebResponse)getRequest.GetResponse (); //THIS LINE/AREA IS CAUSING A LOT OF ISSUES... TRY CATCH NEEDED
					string getString = new StreamReader (getResponse.GetResponseStream()).ReadToEnd();
					if (debug_output) {
						Log.Info(getString + "");
					}
					data = JsonConvert.DeserializeObject<TradeList> (getString);
					break;
				}
				catch (Exception e)
				{
					getRequest.Abort ();
					Log.Error (e.Message);
					if(attempts > 4)
						throw e;
				}
			}

			//Prevent valid trades from being cancelled because they dont exist in tradeStatuses yet
			foreach(TradeData trade in data.trades)
			{
				bool tradeValue;
				if(tradesDone.TryGetValue(trade.tradeid, out tradeValue))
				{
					//Log.Info("Trade " + trade.tradeid + " already made");
				}
				else if(trade.status == 2 || trade.status == 9 || trade.status == 11)
				{
					//Trade not in update list, bot must have restarted, add now
					Log.Info("Incomplete Trade Missing with tradeID: " + trade.tradeid);
					tradesDone[trade.tradeid] = true;
					tradeStatuses[trade.tradeid] = new TradeStatus(trade.user.steamid, trade.steamid, trade.tradeid, trade.type);						
				}
			}

			foreach(TradeData trade in data.trades)
			{
				bool tradeValue;
				if(tradesDone.TryGetValue(trade.tradeid, out tradeValue))
				{
					//Log.Info("Trade " + trade.tradeid + " already made");
				}
				else
				{
					Log.Info("Trade " + trade.tradeid + " not completed... Attempting");

					TradeUser user = trade.user;

					var tradeOffer = Bot.NewTradeOffer(new SteamID(user.steamid));
					int count = 0;
					foreach(long item in trade.items)
					{
						Log.Info("Adding Item with AssetID: " + item + " to Trade");
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
						Log.Info("Deposit/MultiTrade: " + count + " items being traded from user " + user.steamid);
					}
					else if(trade.type.Equals("w", StringComparison.Ordinal))
					{
						Log.Info("Withdrawal: " + count + " items being traded to user " + user.steamid);
					}

					if (tradeOffer.Items.NewVersion)
					{
						string tradeID = "";
						string tradeError = "";
						bool sent = tradeOffer.SendWithToken(out tradeID, out tradeError, trade.user.token, "Secure Code: " + trade.code);

						if(sent)
						{
							Log.Success("New TradeID: " + tradeID);
							tradesDone[trade.tradeid] = true;
							//Add trade to list of trades to check status of (accepted, declined, etc)
							tradeStatuses[trade.tradeid] = new TradeStatus(user.steamid, tradeID, trade.tradeid, trade.type);

							string setTradeIDData = "password=" + password;
							setTradeIDData += "&trade_id=" + trade.tradeid;
							setTradeIDData += "&trade_steam_id=" + tradeID;

							string url = "http://skinbonanza.com/backend/update_trade.php";
							var updaterequest = (HttpWebRequest)WebRequest.Create (url);

							var setTradeID_data = Encoding.ASCII.GetBytes (setTradeIDData);

							updaterequest.Method = "POST";
							updaterequest.ContentType = "application/x-www-form-urlencoded";
							updaterequest.ContentLength = setTradeID_data.Length;

							using (var stream = updaterequest.GetRequestStream ()) {
								stream.Write (setTradeID_data, 0, setTradeID_data.Length);
							}

							for(int attempts = 0;;attempts++)
							{
								try
								{
									var response = (HttpWebResponse)updaterequest.GetResponse ();
									var responseString = new StreamReader (response.GetResponseStream ()).ReadToEnd ();
									if(responseString.Contains("success"))
									{
										Log.Info("Steam TradeID set in update_trade.");
									}
									break;
								}
								catch (Exception e)
								{
									Log.Error (e.Message);
									if(attempts > 4)
										throw e;
								}
							}
						}
						else
						{
							int statusID = 12;
							if(!String.IsNullOrEmpty(tradeError))
							{
								if(tradeError.Contains("JSonException"))
								{
									Log.Error("Trade may have been sent, but a JSON conversion error occurred");
									statusID = 14;
								}
								else if(tradeError.Contains("ResponseEmpty"))
								{
									Log.Error("The request was sent, but Steam returned an empty response");
									statusID = 15;
								}
								else if(tradeError.Contains("(26)"))
								{
									Log.Error("The trade contains an invalid trade item");
									statusID = 16;
								}
								else if(tradeError.Contains("(15)"))
								{
									Log.Error("The trade url for the user is invalid");
									statusID = 17;
								}
								else if(tradeError.Contains("(50)"))
								{
									Log.Error("Too many concurrent trade offers sent (5 max)");
									statusID = 18;
								}
							}

							int knownActiveTradesCount = 0;
							foreach(KeyValuePair<int, TradeStatus> knowntrade in tradeStatuses)
							{
								if(knowntrade.Value.state == TradeOfferState.TradeOfferStateCanceled
									|| knowntrade.Value.state == TradeOfferState.TradeOfferStateCountered
									|| knowntrade.Value.state == TradeOfferState.TradeOfferStateDeclined
									|| knowntrade.Value.state == TradeOfferState.TradeOfferStateExpired
									|| knowntrade.Value.state == TradeOfferState.TradeOfferStateInvalid
									|| knowntrade.Value.state == TradeOfferState.TradeOfferStateInvalidItems
									|| knowntrade.Value.state == TradeOfferState.TradeOfferStateCanceledBySecondFactor
									|| knowntrade.Value.itemsPushed == true)
								{
								}
								else
								{
									knownActiveTradesCount++;
								}
							}

							OffersResponse offersresponse = Bot.tradeOfferManager.webApi.GetActiveTradeOffers(true, false, false);
							List<Offer> activeTrades = offersresponse.TradeOffersSent;
							bool unmatched = false;
							if(activeTrades.Count != knownActiveTradesCount)
							{
								foreach(Offer offer in activeTrades)
								{
									//GetActiveOffers still gets cancelled and declined trades
									//so check that the trade is actually active
									if(offer.TradeOfferState == TradeOfferState.TradeOfferStateActive
										|| offer.TradeOfferState == TradeOfferState.TradeOfferStateNeedsConfirmation
										|| offer.TradeOfferState == TradeOfferState.TradeOfferStateInEscrow)
									{
										foreach(KeyValuePair<int, TradeStatus> checkTrade in tradeStatuses)
										{
											if(checkTrade.Value.state == TradeOfferState.TradeOfferStateCanceled
												|| checkTrade.Value.state == TradeOfferState.TradeOfferStateCountered
												|| checkTrade.Value.state == TradeOfferState.TradeOfferStateDeclined
												|| checkTrade.Value.state == TradeOfferState.TradeOfferStateExpired
												|| checkTrade.Value.state == TradeOfferState.TradeOfferStateInvalid
												|| checkTrade.Value.state == TradeOfferState.TradeOfferStateInvalidItems
												|| checkTrade.Value.state == TradeOfferState.TradeOfferStateCanceledBySecondFactor
												|| checkTrade.Value.itemsPushed == true)
											{
											}
											else
											{
												if(checkTrade.Value.steam_trade_id == offer.TradeOfferId)
												{
													//matching trade found
													continue;
												}
											}
										}

										//no matching trade found
										//trade not being tracked
										unmatched = true;
										Log.Error("Untracked trade found {0}, cancelling it", offer.TradeOfferId);
										Bot.tradeOfferManager.session.Cancel(offer.TradeOfferId);
									}
								}
							}
							if(!unmatched)
								Log.Success("No non-tracked trades found");

							string postData = "password=" + password;
							postData += "&trade_id=" + trade.tradeid;
							postData += "&trade_status=" + statusID;
							postData += "&user_steam_id=" + user.steamid;
							postData += "&bot_steam_id=" + Bot.SteamUser.SteamID.ConvertToUInt64();

							string url = "http://skinbonanza.com/backend/update_trade.php";
							var updaterequest = (HttpWebRequest)WebRequest.Create (url);

							var failedTrade_data = Encoding.ASCII.GetBytes (postData);

							updaterequest.Method = "POST";
							updaterequest.ContentType = "application/x-www-form-urlencoded";
							updaterequest.ContentLength = failedTrade_data.Length;

							using (var stream = updaterequest.GetRequestStream ()) {
								stream.Write (failedTrade_data, 0, failedTrade_data.Length);
							}

							for (int attempts = 0;; attempts++) 
							{
								try 
								{
									var response = (HttpWebResponse)updaterequest.GetResponse ();
									var responseString = new StreamReader (response.GetResponseStream ()).ReadToEnd ();
									if (responseString.Contains ("success")) {
										Log.Success ("Trade status updated.");
									}
									break;
								} catch (Exception e) {
									Log.Error (e.Message);
									if (attempts > 4)
										throw e;
								}
							}
						}
					}
					else
					{
						Log.Error("Old version of trade. Cancelling.");

						string postData = "password=" + password;
						postData += "&trade_id=" + trade.tradeid;
						postData += "&trade_status=" + 6;
						postData += "&user_steam_id=" + user.steamid;
						postData += "&bot_steam_id=" + Bot.SteamUser.SteamID.ConvertToUInt64();

						string url = "http://skinbonanza.com/backend/update_trade.php";
						var updaterequest = (HttpWebRequest)WebRequest.Create (url);

						var failedTrade_data = Encoding.ASCII.GetBytes (postData);

						updaterequest.Method = "POST";
						updaterequest.ContentType = "application/x-www-form-urlencoded";
						updaterequest.ContentLength = failedTrade_data.Length;

						using (var stream = updaterequest.GetRequestStream ()) {
							stream.Write (failedTrade_data, 0, failedTrade_data.Length);
						}

						for (int attempts = 0;; attempts++) 
						{
							try 
							{
								var response = (HttpWebResponse)updaterequest.GetResponse ();
								var responseString = new StreamReader (response.GetResponseStream ()).ReadToEnd ();
								if (responseString.Contains ("success")) {
									Log.Success ("Trade status updated.");
								}
								break;
							} catch (Exception e) {
								Log.Error (e.Message);
								if (attempts > 4)
									throw e;
							}
						}
					}
				}
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

		public void checkTradeStatuses()
		{
			string password = System.IO.File.ReadAllText(@"../cstrade_admin_password.txt");
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
						if (trade.Value.state != tradeOfferData.OfferState) 
						{
							Log.Info ("Trade " + trade.Value.steam_trade_id + "/" + trade.Value.server_trade_id + " has status of " + tradeOfferData.OfferState + "/" + (int)tradeOfferData.OfferState);
							trade.Value.state = tradeOfferData.OfferState;
						} else 
						{
							continue; //go to next trade because state hasnt changed...
						}

						List<long> itemids = new List<long>();
						if(trade.Value.trade_type.Equals("d", StringComparison.Ordinal) ||
						   trade.Value.trade_type.Equals("t", StringComparison.Ordinal) )
						{
							if(tradeOfferData.OfferState == TradeOfferState.TradeOfferStateAccepted)
							{
								for (int attempts = 0;; attempts++) 
								{
									try 
									{
										string tradeReceipt = Bot.SteamWeb.Fetch("http://steamcommunity.com/trade/"+tradeOfferData.TradeID+"/receipt/", "GET", null, false);

										string start = "oItem = ";
										string end = ";";
										string input = tradeReceipt;

										Regex r = new Regex("(?<="+start+")" + "(.*?)" + "(?="+end+")");
										MatchCollection matches = r.Matches(input);
										foreach (Match match in matches) 
										{
											string itemJSON;
											itemJSON = match.Value.Split(',')[0] + "}"; //Strips everything past the id to prevent any issues
											dynamic itemJsonified = JsonConvert.DeserializeObject (itemJSON);
											string itemID = itemJsonified.id;
											long id = Convert.ToInt64 (itemID);
											itemids.Add(id);
										}
										break;
									} catch (Exception e) {
										Log.Error (e.Message);
										if (attempts > 4)
											throw e;
									}
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

						string url = "http://skinbonanza.com/backend/update_trade.php";
						var updaterequest = (HttpWebRequest)WebRequest.Create (url);

						var data = Encoding.ASCII.GetBytes (postData);

						updaterequest.Method = "POST";
						updaterequest.ContentType = "application/x-www-form-urlencoded";
						updaterequest.ContentLength = data.Length;

						using (var stream = updaterequest.GetRequestStream ()) {
							stream.Write (data, 0, data.Length);
						}

						for (int attempts = 0;; attempts++) 
						{
							try 
							{
								var response = (HttpWebResponse)updaterequest.GetResponse ();
								var responseString = new StreamReader (response.GetResponseStream ()).ReadToEnd ();
								if(responseString.Contains("success"))
								{
									Log.Success("Trade " + trade.Value.steam_trade_id + "/" + trade.Value.server_trade_id + " status updated.");
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
								break;
							} catch (Exception e) {
								Log.Error (e.Message);
								if (attempts > 4)
									throw e;
							}
						}
					}
					else
					{
						Log.Warn("Trade offer state request failed.");
					}
				}
			}
		}

		public void Callback( Object state )
		{
			if(Bot.SteamClient.IsConnected)
			{
				// Long running operation
				try
				{
					checkForTrades ();
					checkTradeStatuses ();
					tradeCheckTimer.Change(5000, Timeout.Infinite );
				}
				catch(Exception e) {
					Log.Error (e.Message);
					tradeCheckTimer = new System.Threading.Timer (Callback, null, 5000, Timeout.Infinite );
				}
			}
		}

        public override void OnLoginCompleted()
        {
			tradeCheckTimer = new System.Threading.Timer (Callback, null, 5000, Timeout.Infinite );
			/*tradeCheckTimer. += new ElapsedEventHandler ();
			tradeCheckTimer.Elapsed += new ElapsedEventHandler ();
			tradeCheckTimer.Interval = 5000;
			tradeCheckTimer.Start ();*/
        }

		public override void OnBotCommand(string command)
		{
			if (command.Equals ("genAuthCode", StringComparison.Ordinal)) {
				Log.Success (Bot.SteamGuardAccount.GenerateSteamGuardCode ());
			} else if (command.Equals ("debugOutput", StringComparison.Ordinal)) {
				debug_output = !debug_output;
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
				string password = System.IO.File.ReadAllText(@"../cstrade_admin_password.txt");
				string postData = "password=" + password;
				postData += "&other_steam_id=" + OtherSID.ConvertToUInt64();

				string url = "http://skinbonanza.com/backend/check_bot.php";
				var updaterequest = (HttpWebRequest)WebRequest.Create (url);

				var data = Encoding.ASCII.GetBytes (postData);

				updaterequest.Method = "POST";
				updaterequest.ContentType = "application/x-www-form-urlencoded";
				updaterequest.ContentLength = data.Length;

				using (var stream = updaterequest.GetRequestStream ()) {
					stream.Write (data, 0, data.Length);
				}

				for (int attempts = 0;; attempts++) 
				{
					try 
					{
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
						break;
					} catch (Exception e) {
						Log.Error (e.Message);
						if (attempts > 4)
							throw e;
					}
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

