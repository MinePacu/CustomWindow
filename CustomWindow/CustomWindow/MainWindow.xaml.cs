using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;

namespace CustomWindow
{
    public sealed partial class MainWindow : Window
    {
        private bool _suppressSelectionChange;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Navigation_Loaded(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigated += On_Navigated;
            if (Navigation.MenuItems.FirstOrDefault() is NavigationViewItem first)
            {
                _suppressSelectionChange = true;
                Navigation.SelectedItem = first;
                _suppressSelectionChange = false;
                NavigateToTag(first.Tag?.ToString(), new EntranceNavigationTransitionInfo());
            }
        }

        private void On_Navigated(object sender, NavigationEventArgs e)
        {
            Navigation.IsBackEnabled = ContentFrame.CanGoBack;
            SyncNavigationSelection(e.SourcePageType);
        }

        private void SyncNavigationSelection(Type? pageType)
        {
            if (pageType == null) return;
            var fullName = pageType.FullName;
            var item = Navigation.MenuItems
                                  .OfType<NavigationViewItem>()
                                  .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), fullName, StringComparison.Ordinal));
            if (item != null && (Navigation.SelectedItem as NavigationViewItem) != item)
            {
                _suppressSelectionChange = true;
                Navigation.SelectedItem = item;
                _suppressSelectionChange = false;
            }
        }

        private void Navigation_Navigate(Type navPageType, NavigationTransitionInfo transitionInfo)
        {
            var preNavPageType = ContentFrame.CurrentSourcePageType;
            if (navPageType != null && preNavPageType != navPageType)
            {
                ContentFrame.Navigate(navPageType, null, transitionInfo);
            }
        }

        private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_suppressSelectionChange) return;
            if (args.IsSettingsSelected) return; // settings not implemented
            var tag = args.SelectedItemContainer?.Tag?.ToString();
            NavigateToTag(tag, args.RecommendedNavigationTransitionInfo);
        }

        private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked) return;
            var tag = args.InvokedItemContainer?.Tag?.ToString();
            NavigateToTag(tag, args.RecommendedNavigationTransitionInfo);
        }

        private void NavigateToTag(string? tag, NavigationTransitionInfo transitionInfo)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            var navPageType = Type.GetType(tag);
            if (navPageType == null) return;
            Navigation_Navigate(navPageType, transitionInfo);
        }

        private void n_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) => TryMoveBackPage();

        private bool TryMoveBackPage()
        {
            if (!ContentFrame.CanGoBack) return false;
            if (Navigation.IsPaneOpen && (Navigation.DisplayMode == NavigationViewDisplayMode.Compact || Navigation.DisplayMode == NavigationViewDisplayMode.Minimal))
                return false;
            ContentFrame.GoBack();
            return true;
        }
    }
}
