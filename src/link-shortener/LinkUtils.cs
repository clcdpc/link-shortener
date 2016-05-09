using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using Clc.LinkShortener.Data;

namespace Clc.LinkShortener
{
	public class LinkUtils
	{
		private readonly LinkEntities links;

		public LinkUtils()
		{
			links = new LinkEntities();
		}

		public static string UrlReferrer
		{
			get
			{
				if (HttpContext.Current.Request.UrlReferrer != null)
				{
					return HttpContext.Current.Request.UrlReferrer.ToString();
				}
				else
				{
					return "";
				}
			}
		}

		public static string VisitorIP
		{
			get
			{
				if (HttpContext.Current.Request.ServerVariables["HTTP_X_FORWARDED_FOR"] == null)
				{
					return HttpContext.Current.Request.UserHostAddress;
				}
				else
				{
					return HttpContext.Current.Request.ServerVariables["HTTP_X_FORWARDED_FOR"];
				}
			}
		}
		public string GenerateShortURL()
		{
			var urlLength = 5;
			var rand = new Random();
			const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890";
			var linkBuilder = new StringBuilder();

			for (int i = 0; i < urlLength; i++)
			{
				linkBuilder.Append(chars[rand.Next(chars.Length)]);
			}

			var shortURL = linkBuilder.ToString();


			if (links.Links.Any(l=>l.ShortURL == shortURL))
			{
				shortURL = GenerateShortURL();
			}

			return shortURL;
		}
	}
}
