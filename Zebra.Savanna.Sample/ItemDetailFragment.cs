using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Preferences;
using Android.Text;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using Google.Android.Material.AppBar;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using Zebra.Savanna.Models.Errors;
using Zebra.Savanna.Sample.API;

namespace Zebra.Savanna.Sample
{
    /// <summary>
    /// A fragment representing a single Item detail screen.
    /// This fragment is either contained in a <see cref="ItemListActivity"/>
    /// in two-pane mode (on tablets) or a <see cref="ItemDetailActivity"/>
    /// on handsets.
    /// </summary>
    public class ItemDetailFragment : Fragment, View.IOnClickListener, ITextWatcher
    {
        /// <summary>
        /// The fragment argument representing the item ID that this fragment
        /// represents.
        /// </summary>
        public const string ArgItemId = "item_id";
        public static ItemDetailFragment Instance { get; set; }
        /// <summary>
        /// The item content this fragment is presenting.
        /// </summary>
        private static ApiItem _item;
        private static string _details = "";
        private static Bitmap _barcodeImage;
        private int _density;

        /// <summary>
        /// Mandatory empty constructor for the fragment manager to instantiate the
        /// fragment (e.g. upon screen orientation changes).
        /// </summary>
        public ItemDetailFragment() { }

        /// <summary>
        /// Handle a response from Zebra Savanna APIs
        /// </summary>
        /// <param name="apiData">An object representing a json string, <see cref="byte[]"/>, or <see cref="Error{T}"/>.</param>
        private void OnPostExecute(object apiData, bool showHeader = true)
        {
            ViewGroup root = (ViewGroup)View;
            if (root == null) return;
            TextView barcodeLabel = root.FindViewById<TextView>(Resource.Id.resultLabel);
            barcodeLabel.Visibility = showHeader ? ViewStates.Visible : ViewStates.Gone;
            ImageView barcode = root.FindViewById<ImageView>(Resource.Id.barcode);
            TextView results = root.FindViewById<TextView>(Resource.Id.resultData);
            if (apiData is byte[] data)
            {
                _barcodeImage = BitmapFactory.DecodeByteArray(data, 0, data.Length);
                barcode.SetImageBitmap(_barcodeImage);
                barcode.Visibility = ViewStates.Visible;
                results.Visibility = ViewStates.Gone;
            }
            else if (apiData is Error<string> e)
            {
                string message = e.ErrorDetail ?? e.DeveloperMessage;
                OnPostExecute(message == null || message == e.Message ? e.Message : $"{e.Message}: {message}", false);
            }
            else if (apiData is Error<DeveloperMessage> dm)
            {
                OnPostExecute(dm.DeveloperMessage == null || dm.DeveloperMessage.Fault.FaultString == dm.Message ? dm.Message : $"{dm.Message}: {dm.DeveloperMessage.Fault.FaultString}", false);
            }
            else if (apiData is Exception ex)
            {
                OnPostExecute(ex.Message, false);
            }
            else
            {
                var noResults = Resources.GetString(Resource.String.noResults);
                string json = (string)apiData;
                if (json == "{}")
                {
                    json = string.Empty;
                }
                results.Visibility = ViewStates.Visible;
                if (_details.Length == 0 || _details == noResults)
                {
                    _details = json;
                }
                else if (_details != json && !string.IsNullOrWhiteSpace(json))
                {
                    _details += "\n" + json;
                }
                if (_details.Length == 0)
                {
                    _details = noResults;
                }
                results.Text = _details;
                if (barcode != null)
                {
                    barcode.Visibility = ViewStates.Gone;
                }
            }
        }

        public async void RouteScanData(string barcode, string symbology)
        {
            if (!(View is ViewGroup root)) return;
            TextView results = root.FindViewById<TextView>(Resource.Id.resultData);
            symbology = symbology.Substring("label-type-".Length);
            string upcA = null;
            if (symbology.Equals("upce0"))
            {
                symbology = "upce";
                if (barcode.Length == 6)
                {
                    // Add missing checksum data
                    barcode = BarcodeHelpers.EANChecksum(barcode);
                }
                // Calculate UPC-A code for product lookup
                upcA = BarcodeHelpers.EAN8ToUPCA(barcode);
            }
            switch (_item.Id)
            {
                case "1":
                    EditText barcodeText = root.FindViewById<EditText>(Resource.Id.barcodeText);
                    barcodeText.Text = barcode;

                    Spinner barcodeType = root.FindViewById<Spinner>(Resource.Id.barcodeTypes);
                    int index = Array.IndexOf(Enum.GetValues(typeof(Symbology)), Enum.Parse<Symbology>(symbology.Replace('-', '_')));
                    if (index > -1)
                        barcodeType.SetSelection(index + 1);
                    return;
                case "2":
                    _details = "";
                    results.Text = _details;
                    try
                    {
                        // Call to external Zebra FDA Food Recall API
                        var foodUpcJson = await FDARecall.FoodUpcAsync(barcode);

                        OnPostExecute(JToken.Parse(foodUpcJson).ToString(Formatting.Indented));
                    }
                    catch (Exception e)
                    {
                        OnPostExecute(e);
                    }
                    try
                    {
                        // Call to external Zebra FDA Drug Recall API
                        var drugUpcJson = await FDARecall.DrugUpcAsync(barcode);

                        OnPostExecute(JToken.Parse(drugUpcJson).ToString(Formatting.Indented));
                    }
                    catch (Exception e)
                    {
                        OnPostExecute(e);
                    }
                    return;
                case "3":
                    _details = "";
                    results.Text = _details;
                    EditText upc = root.FindViewById<EditText>(Resource.Id.upc);
                    upc.Text = upcA ?? barcode;
                    try
                    {
                        // Call to external Zebra UPC Lookup API
                        var upcLookupJson = await UPCLookup.LookupAsync(upcA ?? barcode);

                        OnPostExecute(JToken.Parse(upcLookupJson).ToString(Formatting.Indented));
                    }
                    catch (Exception e)
                    {
                        OnPostExecute(e);
                    }
                    return;
            }
        }


        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _density = (int)Math.Ceiling(Resources.DisplayMetrics.Density);
            Instance = this;

            Bundle args = Arguments;
            if (args != null && args.ContainsKey(ArgItemId))
            {
                string key = args.GetString(ArgItemId);

                if (_item != null && key != null && !key.Equals(_item.Id))
                {
                    _barcodeImage = null;
                    _details = "";
                }

                // Load the item content specified by the fragment
                // arguments. In a real-world scenario, use a Loader
                // to load content from a content provider.
                _item = ItemListActivity._content[key];
            }
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var activity = Activity;
            var toolbar = activity.FindViewById<AndroidX.AppCompat.Widget.Toolbar>(Resource.Id.detail_toolbar);
            var toolbarLayout = activity.FindViewById<CollapsingToolbarLayout>(Resource.Id.toolbar_layout);
            switch (_item.Id)
            {
                case "1":
                    activity.Window.SetStatusBarColor(Resources.GetColor(Resource.Color.colorCreateBarcodeDark, activity.Theme));
                    toolbar.SetBackgroundResource(Resource.Color.colorCreateBarcode);
                    toolbarLayout.SetContentScrimColor(Resources.GetColor(Resource.Color.colorCreateBarcode, activity.Theme));
                    toolbarLayout.SetBackgroundResource(Resource.Color.colorCreateBarcode);
                    break;
                case "2":
                    activity.Window.SetStatusBarColor(Resources.GetColor(Resource.Color.colorFdaRecallDark, activity.Theme));
                    toolbar.SetBackgroundResource(Resource.Color.colorFdaRecall);
                    toolbarLayout.SetContentScrimColor(Resources.GetColor(Resource.Color.colorFdaRecall, activity.Theme));
                    toolbarLayout.SetBackgroundResource(Resource.Color.colorFdaRecall);
                    break;
                case "3":
                    activity.Window.SetStatusBarColor(Resources.GetColor(Resource.Color.colorUpcLookupDark, activity.Theme));
                    toolbar.SetBackgroundResource(Resource.Color.colorUpcLookup);
                    toolbarLayout.SetContentScrimColor(Resources.GetColor(Resource.Color.colorUpcLookup, activity.Theme));
                    toolbarLayout.SetBackgroundResource(Resource.Color.colorUpcLookup);
                    break;
            }
            toolbarLayout.SetTitle(_item.Content);

            var root = (ViewGroup)inflater.Inflate(Resource.Layout.item_detail, container, false);
            var sharedPreferences = PreferenceManager.GetDefaultSharedPreferences(Context);

            // Set Zebra Savanna API key
            SavannaAPI.APIKey = sharedPreferences.GetString("apikey", "");

            // Show the item content as text in a TextView.
            if (_item != null)
            {
                root.FindViewById<TextView>(Resource.Id.item_detail).Text = _item.Details;

                switch (_item.Id)
                {
                    case "1":
                        View createView = inflater.Inflate(Resource.Layout.create_barcode, container, false);

                        TextView createResults = createView.FindViewById<TextView>(Resource.Id.resultData);
                        createResults.Text = _details;

                        Button create = createView.FindViewById<Button>(Resource.Id.createBarcode);
                        create.SetOnClickListener(this);

                        Spinner types = createView.FindViewById<Spinner>(Resource.Id.barcodeTypes);
                        Context context = Context;
                        if (context != null)
                        {
                            Array syms = Enum.GetValues(typeof(Symbology));
                            Symbology[] values = new Symbology[syms.Length];
                            syms.CopyTo(values, 0);
                            types.Adapter = new ArrayAdapter(context, Android.Resource.Layout.SimpleSpinnerDropDownItem,
                                values.Select(s => s.ToString().Replace('_', '-'))
                                .Prepend(Resources.GetString(Resource.String.barcode_type)).ToList());
                            types.ItemSelected += Types_ItemSelected;
                        }
                        ImageView barcode = createView.FindViewById<ImageView>(Resource.Id.barcode);
                        if (_barcodeImage == null && _details.Equals(""))
                        {
                            barcode.Visibility = ViewStates.Gone;
                            createResults.Visibility = ViewStates.Visible;
                        }
                        else
                        {
                            barcode.SetImageBitmap(_barcodeImage);
                            barcode.Visibility = ViewStates.Visible;
                            createResults.Visibility = ViewStates.Gone;
                        }
                        root.AddView(createView);
                        break;
                    case "2":
                        View recallView = inflater.Inflate(Resource.Layout.fda_recall, container, false);
                        EditText searchText = recallView.FindViewById<EditText>(Resource.Id.fdaSearchTerm);
                        Button recalls = recallView.FindViewById<Button>(Resource.Id.fdaSearch);
                        recalls.SetOnClickListener(this);
                        searchText.AddTextChangedListener(this);
                        TextView recallResults = recallView.FindViewById<TextView>(Resource.Id.resultData);
                        recallResults.Text = _details;
                        root.AddView(recallView);
                        break;
                    case "3":
                        View lookupView = inflater.Inflate(Resource.Layout.upc_lookup, container, false);
                        TextView results = lookupView.FindViewById<TextView>(Resource.Id.resultData);
                        results.Text = _details;

                        EditText lookupText = lookupView.FindViewById<EditText>(Resource.Id.upc);
                        lookupText.AddTextChangedListener(this);

                        Button lookup = lookupView.FindViewById<Button>(Resource.Id.upc_lookup);
                        lookup.SetOnClickListener(this);

                        root.AddView(lookupView);
                        break;
                }
            }

            return root;
        }

        private void Types_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            if (!(View is ViewGroup root)) return;
            EditText barcodeText = root.FindViewById<EditText>(Resource.Id.barcodeText);
            Button generate = root.FindViewById<Button>(Resource.Id.createBarcode);
            barcodeText.Visibility = e.Position == 0 ? ViewStates.Gone : ViewStates.Visible;
            generate.Enabled = e.Position > 0;
        }

        public void AfterTextChanged(IEditable s) { }

        public void BeforeTextChanged(Java.Lang.ICharSequence s, int start, int count, int after) { }

        public void OnTextChanged(Java.Lang.ICharSequence s, int start, int before, int count)
        {
            if (!(View is ViewGroup root)) return;
            int buttonId;
            switch (_item.Id)
            {
                case "2":
                    buttonId = Resource.Id.fdaSearch;
                    break;
                case "3":
                    buttonId = Resource.Id.upc_lookup;
                    break;
                default:
                    return;
            }
            var button = root.FindViewById<Button>(buttonId);
            button.Enabled = s.ToString().Trim().Length > 0;
        }

        public async void OnClick(View v)
        {
            if (!(View is ViewGroup root)) return;

            TextView results = root.FindViewById<TextView>(Resource.Id.resultData);
            _details = "";
            results.Text = _details;
            switch (_item.Id)
            {
                case "1":
                    EditText barcodeText = root.FindViewById<EditText>(Resource.Id.barcodeText);
                    Spinner barcodeType = root.FindViewById<Spinner>(Resource.Id.barcodeTypes);
                    CheckBox includeText = root.FindViewById<CheckBox>(Resource.Id.includeText);
                    var symbology = Enum.Parse<Symbology>(barcodeType.SelectedItem.ToString().Replace('-', '_'));
                    try
                    {
                        // Call to external Zebra Create Barcode API
                        var barcodeBytes = await CreateBarcode.CreateAsync(symbology, barcodeText.Text, _density * 3, Rotation.Normal, includeText.Checked);

                        OnPostExecute(barcodeBytes);
                    }
                    catch (Exception e)
                    {
                        OnPostExecute(e);
                    }
                    return;
                case "2":
                    EditText searchText = root.FindViewById<EditText>(Resource.Id.fdaSearchTerm);
                    try
                    {
                        // Call to external Zebra FDA Device Recall Search API
                        var deviceSearchJson = await FDARecall.DeviceSearchAsync(searchText.Text);

                        OnPostExecute(JToken.Parse(deviceSearchJson).ToString(Formatting.Indented));
                    }
                    catch (Exception e)
                    {
                        OnPostExecute(e);
                    }

                    try
                    {
                        // Call to external Zebra FDA Drug Recall Search API
                        var drugSearchJson = await FDARecall.DrugSearchAsync(searchText.Text);

                        OnPostExecute(JToken.Parse(drugSearchJson).ToString(Formatting.Indented));
                    }
                    catch (Exception e)
                    {
                        OnPostExecute(e);
                    }
                    return;
                case "3":
                    EditText lookupText = root.FindViewById<EditText>(Resource.Id.upc);
                    try
                    {
                        // Call to external Zebra UPC Lookup API
                        var upcLookupJson = await UPCLookup.LookupAsync(lookupText.Text);

                        OnPostExecute(JToken.Parse(upcLookupJson).ToString(Formatting.Indented));
                    }
                    catch (Exception e)
                    {
                        OnPostExecute(e);
                    }
                    return;
            }
        }
    }
}