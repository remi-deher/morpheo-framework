using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;
using Morpheo.Core;
using Morpheo.Core.Data; // Pour SyncLog
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Morpheo.App;

public partial class MainWindow : Window
{
    private readonly MorpheoNode _node;

    public ObservableCollection<Product> Products { get; set; } = new();

    public MainWindow(MorpheoNode node, MorpheoOptions options)
    {
        InitializeComponent();
        _node = node;

        TxtNodeName.Text = options.NodeName;
        LstProducts.ItemsSource = Products;

        // 1. Découverte (UI Update)
        _node.Discovery.PeerFound += (s, p) => Dispatcher.Invoke(UpdateStatus);
        _node.Discovery.PeerLost += (s, p) => Dispatcher.Invoke(UpdateStatus);

        // 2. Synchronisation TEMPS RÉEL (Plus de Timer !)
        // On s'abonne à l'événement que nous venons de créer
        _node.Sync.DataReceived += OnSyncDataReceived;

        // 3. Chargement initial (Pour voir les produits existants au lancement)
        Loaded += async (s, e) => await LoadInitialHistory();
    }

    // Cette méthode est appelée AUTOMATIQUEMENT par le Framework quand une donnée arrive
    private void OnSyncDataReceived(object? sender, SyncLog log)
    {
        // IMPORTANT : L'événement arrive depuis un Thread Réseau (Background).
        // WPF interdit de toucher à l'UI depuis un autre thread.
        // On utilise Dispatcher.Invoke pour revenir sur le thread principal.
        Dispatcher.Invoke(() =>
        {
            ProcessLog(log);
        });
    }

    private void ProcessLog(SyncLog log)
    {
        if (log.EntityName == "Product" && log.Action == "CREATE")
        {
            // On évite les doublons visuels
            if (Products.Any(p => p.Id == log.EntityId)) return;

            try
            {
                var p = JsonSerializer.Deserialize<Product>(log.JsonData);
                if (p != null)
                {
                    Products.Add(p);
                    // Petit effet visuel : Scroll vers le bas
                    LstProducts.ScrollIntoView(p);
                }
            }
            catch { /* Ignorer json invalide */ }
        }
    }

    private async Task LoadInitialHistory()
    {
        using var scope = App.AppHost!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var logs = await db.SyncLogs
            .Where(l => l.EntityName == "Product" && l.Action == "CREATE")
            .ToListAsync();

        foreach (var log in logs) ProcessLog(log);
    }

    private void UpdateStatus()
    {
        int count = _node.Discovery.GetPeers().Count;
        TxtStatus.Text = $"{count} voisin(s) connecté(s)";
        TxtStatus.Foreground = count > 0 ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
    }

    private async void BtnAddProduct_Click(object sender, RoutedEventArgs e)
    {
        var p = new Product
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Café " + DateTime.Now.ToLongTimeString(), // LongTime pour voir les secondes bouger
            Price = 2.50m
        };

        // Ajout Local
        Products.Add(p);
        LstProducts.ScrollIntoView(p);

        // Propagation
        await _node.Sync.BroadcastChangeAsync(p, "CREATE");
    }
}