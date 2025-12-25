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

namespace Morpheo.App;

public partial class MainWindow : Window
{
    private readonly MorpheoNode _node;

    // Si cette variable contient un ID, c'est qu'on est en train de modifier ce produit
    private string? _editingProductId = null;

    // Pour la simulation dashboard
    private int _simulatedClientCount = 0;

    public ObservableCollection<Product> Products { get; set; } = new();

    public MainWindow(MorpheoNode node, MorpheoOptions options)
    {
        InitializeComponent();
        _node = node;

        TxtNodeName.Text = options.NodeName;
        LstProducts.ItemsSource = Products;

        // Événements Réseau
        _node.Discovery.PeerFound += (s, p) => Dispatcher.Invoke(UpdateStatus);
        _node.Discovery.PeerLost += (s, p) => Dispatcher.Invoke(UpdateStatus);
        _node.Sync.DataReceived += OnSyncDataReceived;

        Loaded += async (s, e) => await LoadInitialHistory();
    }

    // ---------------------------------------------------------
    // SYNCHRONISATION (CREATE / UPDATE / DELETE)
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
            // Mise à jour d'un produit existant
            var existing = Products.FirstOrDefault(x => x.Id == log.EntityId);
            if (existing != null)
            {
                try
                {
                    var updated = JsonSerializer.Deserialize<Product>(log.JsonData);
                    if (updated != null)
                    {
                        // Astuce : On remplace l'objet dans la liste pour forcer le rafraichissement visuel
                        int index = Products.IndexOf(existing);
                        Products[index] = updated;
                    }
                }
                catch { }
            }
        }
    }

    // ---------------------------------------------------------
    // GESTION UI : AJOUT / MODIFICATION
    // ---------------------------------------------------------

    // C'est le même bouton qui sert à Ajouter OU Sauvegarder la modif
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
            // === MODE CRÉATION ===
            var p = new Product
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Price = price
            };

            Products.Add(p); // Local
            LstProducts.ScrollIntoView(p);
            await _node.Sync.BroadcastChangeAsync(p, "CREATE"); // Réseau
        }
        else
        {
            // === MODE MODIFICATION ===
            var existing = Products.FirstOrDefault(p => p.Id == _editingProductId);
            if (existing != null)
            {
                // On met à jour l'objet local
                // Note : Pour que l'UI se mette à jour, on crée un nouvel objet ou on implémente INotifyPropertyChanged.
                // Ici on va remplacer l'objet dans la liste pour faire simple.
                var updated = new Product
                {
                    Id = existing.Id,
                    Name = name,
                    Price = price
                };

                int index = Products.IndexOf(existing);
                Products[index] = updated; // Update Local Visuel

                await _node.Sync.BroadcastChangeAsync(updated, "UPDATE"); // Réseau
            }

            // On quitte le mode édition
            StopEditing();
        }

        // Reset du formulaire
        if (_editingProductId == null)
        {
            TxtNewProductName.Text = "Nouveau Produit";
            TxtNewProductPrice.Text = "0";
        }
    }

    // Bouton Crayon (Dans la liste)
    private void BtnEditProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Product productToEdit)
        {
            // 1. On remplit le formulaire avec les infos
            TxtNewProductName.Text = productToEdit.Name;
            TxtNewProductPrice.Text = productToEdit.Price.ToString();

            // 2. On stocke l'ID
            _editingProductId = productToEdit.Id;

            // 3. On change l'aspect visuel pour dire "On est en train de modifier"
            BtnSave.Content = "💾 Enregistrer";
            BtnSave.Background = System.Windows.Media.Brushes.Orange;
            BtnCancelEdit.Visibility = Visibility.Visible;

            // Focus sur le champ nom pour taper direct
            TxtNewProductName.Focus();
        }
    }

    // Bouton Annuler (Croix)
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
            // Si on supprime celui qu'on est en train de modifier, on annule l'édition
            if (_editingProductId == productToDelete.Id) StopEditing();

            Products.Remove(productToDelete);
            await _node.Sync.BroadcastChangeAsync(productToDelete, "DELETE");
        }
    }

    // ---------------------------------------------------------
    // RESTE DU CODE (Similaire à avant)
    // ---------------------------------------------------------
    private async Task LoadInitialHistory()
    {
        using var scope = App.AppHost!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Simplifié : on recharge les CREATE
        var logs = await db.SyncLogs.Where(l => l.EntityName == "Product" && l.Action == "CREATE").ToListAsync();
        foreach (var log in logs) ProcessLog(log);
    }

    private void UpdateStatus()
    {
        int count = _node.Discovery.GetPeers().Count;
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