using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Shapes;
using System.Runtime.Intrinsics.Arm;
using System.Threading.Tasks;
using System.Text.Json;

namespace ArkhamDisplay
{
	public partial class KnightWindow : BaseWindow
	{
		private static readonly HttpClient imageClient = new HttpClient();
		private readonly string imageCacheDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageCache");
		public KnightWindow() : base(Game.Knight)
		{
			InitializeComponent();
			PostInitialize();
		}
		protected override SaveParser CreateSaveParser()
		{
			return new KnightSave(Data.SaveLocations[(int)game], Data.SaveIDs[(int)game]);
		}

		protected override string GetEntryName(Entry entry)
		{
			if (TwoFortyPercentMenuItem.IsChecked && "NG+".Equals(entry.metadata))
			{
				return entry.name + " (" + entry.metadata + ")";
			}
			return base.GetEntryName(entry);
		}

		protected override void SetCurrentRoute()
		{
			if (Data.KnightFirstEnding)
			{
				currentRoute = "KnightFirstEnding";
			}
			else if (Data.KnightNGPlus)
			{
				currentRoute = "KnightNG+";
			}
			else if (Data.Knight240)
			{
				currentRoute = "Knight240";
			}
			else if (Data.KnightMoF)
			{
				currentRoute = "KnightMoF";
			}
			else
			{
				currentRoute = "KnightDefault";
			}

			Data.CurrentRoute = currentRoute;
		}

		protected override void UpdateUI()
		{
			FirstEndingMenuItem.IsChecked = Data.KnightFirstEnding;
			NGPlusMenuItem.IsChecked = Data.KnightNGPlus;
			TwoFortyPercentMenuItem.IsChecked = Data.Knight240;
			MatterOfFamilyMenuItem.IsChecked = Data.KnightMoF;
			base.UpdateUI();
			updateNextStepImageVisibility();
		}

		protected override void UpdatePreferences(object sender = null, RoutedEventArgs e = null)
		{
			//Some settings are incompatible, so if one switches one make sure the other does too
			if (sender == FirstEndingMenuItem && FirstEndingMenuItem.IsChecked)
			{
				TwoFortyPercentMenuItem.IsChecked = false;
				MatterOfFamilyMenuItem.IsChecked = false;
			}
			else if (sender == TwoFortyPercentMenuItem && TwoFortyPercentMenuItem.IsChecked)
			{
				NGPlusMenuItem.IsChecked = false;
				FirstEndingMenuItem.IsChecked = false;
				MatterOfFamilyMenuItem.IsChecked = false;
			}
			else if (sender == MatterOfFamilyMenuItem && MatterOfFamilyMenuItem.IsChecked)
			{
				NGPlusMenuItem.IsChecked = false;
				FirstEndingMenuItem.IsChecked = false;
				TwoFortyPercentMenuItem.IsChecked = false;
			}
			else if (sender == NGPlusMenuItem && NGPlusMenuItem.IsChecked)
			{
				TwoFortyPercentMenuItem.IsChecked = false;
				MatterOfFamilyMenuItem.IsChecked = false;
			}
			

			// TODO: We can probably get rid of this
			if (NGPlusMenuItem.IsChecked)
			{
				minRequiredMatches = 2;
			}
			else
			{
				minRequiredMatches = 1;
			}

			Data.KnightFirstEnding = FirstEndingMenuItem.IsChecked;
			Data.KnightNGPlus = NGPlusMenuItem.IsChecked;
			Data.Knight240 = TwoFortyPercentMenuItem.IsChecked;
			Data.KnightMoF = MatterOfFamilyMenuItem.IsChecked;

			base.UpdatePreferences(sender, e);
		}

		protected override void UpdatePercent(int doneEntries, int totalEntries)
		{
			base.UpdatePercent(doneEntries, totalEntries);
			//removing this for two reasons:
			//1) I'd rather see the progress through the current route im following
			//2) I get that 240%'s whole thing is the save file percent being stupidly large and therefore fun
			//   but this hack implemented here does not even get the correct percentage of the savefile
			//   and i dont want to reverse engineer how the game calculates it, as that involves quiting out many many times
			//   i suspect the game has specific checks that are worth a flat percentage rather than an actual complete/total calculation
			//   for example: each Super villan in season of infamy is worth 5%, but only when their whole questline is done, none of the steps add any percentage
			//   this might be unique to the dlc missions, but i dont wanna go through all the testing needed for the base game
			// double percentDone = 100.0 * doneEntries / totalEntries;
			// if (TwoFortyPercentMenuItem.IsChecked)
			// {
			// 	// TODO: This is just a hack. Ideally, we'd know the number of rows that are NG+
			// 	// and scale that to be 120%, while the remaining would scale to 120%. However,
			// 	// that's too much work for me to bother.
			// 	int newGameEntries = 532;
			// 	MessageBox.Show("Done: " + doneEntries + "\nTotal: " + totalEntries);

			// 	if (doneEntries <= newGameEntries)
			// 	{
			// 		// The number of newGame entries should be equal to 119%
			// 		percentDone = 119.0 * doneEntries / newGameEntries;
			// 	}
			// 	else
			// 	{
			// 		// The remaining entries (totalEntries - newGameEntries) should be equal to 121%
			// 		percentDone = 119.0 + (doneEntries - newGameEntries) * 121 / (totalEntries - newGameEntries);
			// 	}
			// 	//percentDone = get240Completion();
			// }

			// if (percentDone >= 100.0 && !Data.Knight240)
			// {
			// 	progressCounter.Text = string.Format("{0:0}", percentDone) + "%";
			// }
			// else
			// {
			// 	progressCounter.Text = string.Format("{0:0.0}", percentDone) + "%";
			// }

			// riddleCounter.Text = GetRiddleCount();
		}

		protected override string GetRiddleCount()
		{
			return saveParser.GetLastMatch(@"\b\d*\/243\b");
		}


		private async void UpdateNextStepImage()
		{
			CacheImagesButton.Visibility = Visibility.Visible;
			List<Entry> routeEntries = GetEntriesForDisplay(Data.GetRoute(currentRoute)); //grabs entries for current route discluding placeholders
			if (routeEntries == null || routeEntries.Count == 0)
			{
				NextStepImage.Source = null;
				NextStepText.Text = "No Route Entry";
				NextNextStepImage.Source = null;
				NextNextStepText.Text = "No Route Entry";
				NextStepIdentifierText.Visibility = Visibility.Collapsed;
				NextNextStepIdentifierText.Visibility = Visibility.Collapsed;
				NextStepSeparator.Visibility = Visibility.Collapsed;
				CacheImagesButton.Visibility = Visibility.Collapsed;
				return;
			}
			int currentStepIndex = -1;
			int nextStepIndex = -1;

			//check for the next 2 incomplete steps, aka the current and next steps
			for (int i = 0; i < routeEntries.Count; i++)
			{
				if (Data.IgnoreTypes.Contains(currentRoute + routeEntries[i].type)) //respect ignored categories
				{
					continue;
				}
				if (!saveParser.HasKey(routeEntries[i], minRequiredMatches))
				{
					if (currentStepIndex == -1)
					{
						currentStepIndex = i;
					}
					else
					{
						nextStepIndex = i; //we found them both
						break; //break makes it so it stops checking entirely
					}
				}
			}

			//check for no incomplete entries found
			if (currentStepIndex == -1)
			{
				NextStepImage.Source = null;
				NextStepText.Text = "No incomplete steps. \n\nAll Done!";
				NextNextStepImage.Source = null;
				NextNextStepText.Text = null;
				NextStepIdentifierText.Visibility = Visibility.Collapsed;
				NextNextStepIdentifierText.Visibility = Visibility.Collapsed;
				NextStepSeparator.Visibility = Visibility.Collapsed;
				return;
			}
			//print current image
			await grabImage(routeEntries[currentStepIndex], NextStepText, NextStepImage);
			NextStepIdentifierText.Visibility = Visibility.Visible;
			NextStepIdentifierText.Text = routeEntries[currentStepIndex].name + ":";

			//check and print second image
			if (nextStepIndex == -1) //if this triggers, it means there is only 1 incomplete step left
			{
				NextNextStepImage.Source = null;
				NextNextStepText.Text = "No next step.";
				NextStepIdentifierText.Visibility = Visibility.Visible;
				NextStepSeparator.Visibility = Visibility.Visible;
			}
			else //shouldn't call this without checking the first step
			{
				await grabImage(routeEntries[nextStepIndex], NextNextStepText, NextNextStepImage);
				NextNextStepIdentifierText.Text = routeEntries[nextStepIndex].name + ":";
				NextStepIdentifierText.Visibility = Visibility.Visible;     //if you have the second one you should have the first for readability
				NextNextStepIdentifierText.Visibility = Visibility.Visible;
				NextStepSeparator.Visibility = Visibility.Visible;
			}
		}
		
		private async Task grabImage(Entry entry, TextBlock tempTextBlock, Image tempImage)
		{
			string routeImageCacheDirectory = System.IO.Path.Combine(imageCacheDirectory, getGameName());
			tempTextBlock.Text = entry.name;
			if (string.IsNullOrWhiteSpace(entry.image))
			{
				tempImage.Source = null;
				tempTextBlock.Text = entry.name + "\n\nNo image";
				return;
			}
			try
			{
				Directory.CreateDirectory(routeImageCacheDirectory);
				string hash;
				using (SHA256 sha = SHA256.Create())
				{
					byte[] bytes = System.Text.Encoding.UTF8.GetBytes(entry.image);
					byte[] hashbytes = sha.ComputeHash(bytes);
					hash = Convert.ToHexString(hashbytes);
				}
				string extension = System.IO.Path.GetExtension(new Uri(entry.image).AbsolutePath);
				if (string.IsNullOrWhiteSpace(extension))
				{
					extension = ".img";
				}
				string cachePath = System.IO.Path.Combine(routeImageCacheDirectory, hash + extension);
				if (!File.Exists(cachePath))
				{
					byte[] imageData = await imageClient.GetByteArrayAsync(entry.image);
					File.WriteAllBytes(cachePath, imageData);
				}

				System.Windows.Media.Imaging.BitmapImage image = new System.Windows.Media.Imaging.BitmapImage();
				image.BeginInit();
				image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
				image.UriSource = new Uri(cachePath, UriKind.Absolute);
				image.EndInit();
				tempImage.Source = image;
				tempTextBlock.Text = ""; //image grabbing should succeed
			}
			catch (Exception ex)
			{
				tempImage.Source = null;
				tempTextBlock.Text = entry.name + "\n\nImage Failed to load:\n" + ex.Message;
			}
		}
		private void CacheImagesButton_Click(object sender, RoutedEventArgs e)
		{
			List<Entry> routeEntries = GetEntriesForDisplay(Data.GetRoute(currentRoute));
			CacheConfirmationWindow confirmationWindow = new CacheConfirmationWindow(routeEntries, currentRoute, getGameName());
			confirmationWindow.Owner = this;
			bool? result = confirmationWindow.ShowDialog();
			if (result == true)
			{
				//
			}
		}
		

		protected override void UpdateRouteWindow()
		{
			base.UpdateRouteWindow();
			if(Data.ShowImages) //only try to fetch/display images if user wants them displayed
			{
				UpdateNextStepImage();
			}
		}

		private void updateNextStepImageVisibility()
		{
			if (Data.ShowImages)
			{
				ImageDisplayColumn.Visibility = Visibility.Visible;
			}
			else
			{
				ImageDisplayColumn.Visibility = Visibility.Collapsed;
			}
		}
	}
}