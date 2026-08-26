using System;
using System.Collections.Generic;
using System.Windows;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading;

namespace ArkhamDisplay
{
    public partial class CacheConfirmationWindow : Window
    {
        private List<Entry> routeEntries = new List<Entry>();
		private List<Entry> failedEntries = new List<Entry>(); 
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		private bool isCaching = false;

        private static readonly HttpClient imageClient = new HttpClient();
		private readonly string imageCacheDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageCache");
        public CacheConfirmationWindow(List<Entry> routeEntries)
        {
            InitializeComponent();
            this.routeEntries = routeEntries;
        }

        private async void YesButton_Click(object sender, RoutedEventArgs e)
        {
            CacheConfirmation.Visibility = Visibility.Collapsed;
            Height = 300;
            CacheProgress.Visibility = Visibility.Visible;
			isCaching = true;
            await CacheAllImages();
			if (isCaching)
			{
				StatusText.Text = "Finished caching images!";
				
			}else
			{
				StatusText.Text = "Canceled Image Caching";
			}
            CancelButton.Content = "Close";
			isCaching = false;
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
		private void ClearCache_Click(object sender, RoutedEventArgs e)
		{
			if (!Directory.Exists(imageCacheDirectory))
			{
				CacheClearedConfirmation.Text = "No Cache Directory";
				CacheClearedConfirmation.Visibility = Visibility.Visible;
				return;
			}
			Directory.Delete(imageCacheDirectory, true);
			CacheClearedConfirmation.Visibility = Visibility.Visible;
		}

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
			if (isCaching)
            {
				cancellationTokenSource.Cancel();
				isCaching = false;
			}else
			{
				DialogResult = false;
				Close();
			}
        }
        private async Task CacheAllImages()
		{
			Directory.CreateDirectory(imageCacheDirectory);
			failedEntries.Clear();
			// create the path for the JSON file report
			// containing the hash/filename and original URL
			string jsonPath =
				System.IO.Path.Combine(
					imageCacheDirectory,
					"imageCacheReport.json"
				);
			
			if (File.Exists(jsonPath))
			{
				File.Delete(jsonPath); //delete old report
			}
			//dictionary keys are unique, so duplicates are no issue,
			Dictionary<string, string> imageCache = new Dictionary<string, string>();
			
            
			HashSet<string> uniqueImages = new HashSet<string>();
			HashSet<string> processedImages = new HashSet<string>();
            foreach (Entry entry in routeEntries)
            {
                if(!string.IsNullOrWhiteSpace(entry.image))
                {
                    uniqueImages.Add(entry.image);
                }
            }

            CacheProgressBar.Maximum = uniqueImages.Count;
            CacheProgressBar.Value = 0;
            ProgressCountText.Text = $"0 / {uniqueImages.Count} images cached";

			//processing the images
			for (int i = 0; i < routeEntries.Count; i++)
			{
                if (cancellationTokenSource.Token.IsCancellationRequested)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(routeEntries[i].image))
                {
                    continue;
                }
				//adds to processedImage count, and checks for duplicate images
				if (!processedImages.Add(routeEntries[i].image))
                {
                    continue;
                }

                StatusText.Text = $"Caching: \n{routeEntries[i].name}";
                await GrabImageforCache(routeEntries[i], cancellationTokenSource.Token, imageCache);
                CacheProgressBar.Value = processedImages.Count;
                ProgressCountText.Text = $"{processedImages.Count} / {uniqueImages.Count} images processed";
			}
			//write failed entries at the end, if any
			foreach(Entry entry in failedEntries)
			{
				imageCache["Error: " + entry.name+ " - " + entry.id + " - " + entry.metadata] = entry.image;

			}
			// Convert JSON dictionary to string
			string formattedJson =
				JsonSerializer.Serialize(
					imageCache,
					new JsonSerializerOptions
					{
						WriteIndented = true
					}
				);
			//write string to file
			File.WriteAllText(jsonPath, formattedJson);
		}

		private async Task GrabImageforCache(Entry entry, CancellationToken cancellationToken, Dictionary<string, string> imageCache)
		{
			if (string.IsNullOrWhiteSpace(entry.image))
			{
				return;
			}

			try
			{
				string hash;

				using (SHA256 sha = SHA256.Create())
				{
					byte[] bytes = System.Text.Encoding.UTF8.GetBytes(entry.image);
					byte[] hashbytes = sha.ComputeHash(bytes);
					hash = Convert.ToHexString(hashbytes);
				}

				string extension =
					System.IO.Path.GetExtension(
						new Uri(entry.image).AbsolutePath
					);

				if (string.IsNullOrWhiteSpace(extension))
				{
					extension = ".img";
				}

				string cachePath =
					System.IO.Path.Combine(
						imageCacheDirectory,
						hash + extension
					);

				// Download the image if it isn't already cached
				if (!File.Exists(cachePath))
				{
					byte[] imageData =
						await imageClient.GetByteArrayAsync(entry.image, cancellationToken);

					File.WriteAllBytes(cachePath, imageData);
				}
				// Add this image to the mapping
				imageCache[hash + extension] = entry.image;
			}
			catch(OperationCanceledException)
			{
				//dont count cancellation as a failed image
				return;
			}
			catch
			{
				//image failed to download
				failedEntries.Add(entry);
				return;
			}
		}
    }
    
}