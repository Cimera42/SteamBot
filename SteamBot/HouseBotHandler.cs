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
	public class HouseBotHandler : UserHandler
	{
		public HouseBotHandler (Bot bot, SteamID sid) : base(bot, sid) {}

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

		public override void OnLoginCompleted() {}
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

