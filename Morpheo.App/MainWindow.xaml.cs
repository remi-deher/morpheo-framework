using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;
using Morpheo.Core;
using Morpheo.Core.Data;
using Morpheo.Core.Sync; // Pour DataSyncService

namespace Morpheo.App;

public partial class MainWindow : Window
{
    // On remplace _node par les services précis dont on a besoin
    private readonly INetworkDiscovery _discovery;
    private readonly DataSyncService _syncService;

    private string? _editingProductId = null;
    private int _simulatedClientCount = 0;

    public ObservableCollection<Product> Products { get; set; } = new();

    // Injection directe des services via le constructeur
    public MainWindow(
        INetworkDiscovery discovery,
        DataSyncService syncService,
        MorpheoOptions options)
    {
        InitializeComponent();

        _discovery = discovery;
        _syncService = syncService;

        TxtNodeName.Text = options.NodeName;
        LstProducts.ItemsSource = Products;

        // Événements Réseau
        _discovery.PeerFound += (s, p) => Dispatcher.Invoke(UpdateStatus);
        _discovery.PeerLost += (s, p) => Dispatcher.Invoke(UpdateStatus);
        _syncService.DataReceived += OnSyncDataReceived;

        Loaded += async (s, e) => await LoadInitialHistory();
    }

    // ---------------------------------------------------------
    // SYNCHRONISATION
    // ---------------------------------------------------------

    private void OnSyncDataReceived(object? sender, SyncLog log)
    {
        Dispatcher.Invoke(() => ProcessLog(log));
    }

    private void ProcessLog(SyncLog log)
    {
        if (log.EntityName != "Product") return;

        if (log.Action == "CREATE")
        {
            if (Products.Any(p => p.Id == log.EntityId)) return;
            try
            {
                var p = JsonSerializer.Deserialize<Product>(log.JsonData);
                if (p != null)
                {
                    Products.Add(p);
                    LstProducts.ScrollIntoView(p);
                }
            }
            catch { }
        }
        else if (log.Action == "DELETE")
        {
            var p = Products.FirstOrDefault(x => x.Id == log.EntityId);
            if (p != null) Products.Remove(p);
        }
        else if (log.Action == "UPDATE")
        {
            var existing = Products.FirstOrDefault(x => x.Id == log.EntityId);
            if (existing != null)
            {
                try
                {
                    var updated = JsonSerializer.Deserialize<Product>(log.JsonData);
                    if (updated != null)
                    {
                        int index = Products.IndexOf(existing);
                        Products[index] = updated;
                    }
                }
                catch { }
            }
        }
    }

    // ---------------------------------------------------------
    // GESTION UI
    // ---------------------------------------------------------

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtNewProductName.Text;
        string priceStr = TxtNewProductPrice.Text;

        if (string.IsNullOrWhiteSpace(name) || !decimal.TryParse(priceStr, out decimal price))
        {
            MessageBox.Show("Nom ou prix invalide.");
            return;
        }

        if (_editingProductId == null)
        {
            var p = new Product
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Price = price
            };

            Products.Add(p);
            LstProducts.ScrollIntoView(p);
            // Utilisation du service injecté
            await _syncService.BroadcastChangeAsync(p, "CREATE");
        }
        else
        {
            var existing = Products.FirstOrDefault(p => p.Id == _editingProductId);
            if (existing != null)
            {
                var updated = new Product
                {
                    Id = existing.Id,
                    Name = name,
                    Price = price
                };

                int index = Products.IndexOf(existing);
                Products[index] = updated;

                // Utilisation du service injecté
                await _syncService.BroadcastChangeAsync(updated, "UPDATE");
            }
            StopEditing();
        }

        if (_editingProductId == null)
        {
            TxtNewProductName.Text = "Nouveau Produit";
            TxtNewProductPrice.Text = "0";
        }
    }

    private void BtnEditProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Product productToEdit)
        {
            TxtNewProductName.Text = productToEdit.Name;
            TxtNewProductPrice.Text = productToEdit.Price.ToString();
            _editingProductId = productToEdit.Id;
            BtnSave.Content = "💾 Enregistrer";
            BtnSave.Background = System.Windows.Media.Brushes.Orange;
            BtnCancelEdit.Visibility = Visibility.Visible;
            TxtNewProductName.Focus();
        }
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
    {
        StopEditing();
    }

    private void StopEditing()
    {
        _editingProductId = null;
        BtnSave.Content = "➕ Ajouter";
        BtnSave.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1A73E8")!;
        BtnCancelEdit.Visibility = Visibility.Collapsed;
        TxtNewProductName.Text = "Nouveau Produit";
        TxtNewProductPrice.Text = "0";
    }

    private async void BtnDeleteProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Product productToDelete)
        {
            if (_editingProductId == productToDelete.Id) StopEditing();
            Products.Remove(productToDelete);
            await _syncService.BroadcastChangeAsync(productToDelete, "DELETE");
        }
    }

    private async Task LoadInitialHistory()
    {
        using var scope = App.AppHost!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.SyncLogs.Where(l => l.EntityName == "Product" && l.Action == "CREATE").ToListAsync();
        foreach (var log in logs) ProcessLog(log);
    }

    private void UpdateStatus()
    {
        int count = _discovery.GetPeers().Count;
        TxtStatus.Text = $"{count} voisin(s)";
        TxtStatus.Foreground = count > 0 ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
    }

    private void BtnSimulateClient_Click(object sender, RoutedEventArgs e)
    {
        _simulatedClientCount++;
        LblClientCount.Text = $"Clients actifs : {_simulatedClientCount}";
    }

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        BtnSync.IsEnabled = false;
        await Task.Delay(1000);
        BtnSync.IsEnabled = true;
    }

    private void BtnAttack_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Sécurité : Attaque bloquée.", "Morpheo");
    }
}