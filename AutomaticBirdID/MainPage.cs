using ClosedXML.Excel;
using Newtonsoft.Json.Linq;
using ScottPlot;
using ScottPlot.WinForms;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AutomaticBirdID.MainPage.DataBlock;

namespace AutomaticBirdID
{

    public partial class MainPage
        : Form
    {
        //file paths
        private List<string> selectedFilePathsFullList = new List<string>();
        private string rareBirdsListFilePath = "";
        private string settingsFilePath = "";
        private string BirdIDDataBase = "";

        //python exe 
        private string pythonAnalyserExeAddress = "";

        private readonly object _resultsLock = new object();
        private Dictionary<string, List<AnalysisResult>> _allResults = new Dictionary<string, List<AnalysisResult>>();

        //file saving
        private string defaultLocation = "";
        private string currentLocationSelection = "";

        private string outputFilePath = "Desktop";
        private string inputFilePath = "null";

        //analysis arguments
        private double confidenceValue = 0.25;
        private double latitude = 0.0000;
        private double longitude = 0.0000;

        //argument settings
        private List<Location> savedLocations = new List<Location>();
        private bool useByTimeOfYearSpecies = false;
        private double sensitivity = 1;
        private double Overlap = 0;

        //data proccessing 
        private DataBlock mainDataBlock = new DataBlock();
        private List<DataBlock> liveDatablocks = new List<DataBlock>();
        private int intervalsPerHour = 15;
        private List<string> allaxisoptions = new List<string>();
        private List<string> allGraphTypes = new List<string>();
        private string birdnetPackageVersion = "1.16.1";
        private string selectedSpecies = "";

        //other
        private Stopwatch _stopwatch;
        private int lastTimeCheck = 0;
        private List<rareSpecies> rareSpeciesList = new List<rareSpecies>();
        private List<string> birdsNotVisible = new List<string>();

        //main graph
        public FormsPlot graph1 = new FormsPlot() { Dock = DockStyle.Fill };

        //save vairiables
        private string speciesMetricSelectedSpeices = null;


        /// <summary>
        /// enitialise page + stopwatch
        /// </summary>
        public MainPage()
        {
            InitializeComponent();

            //stopwatch setup
            _stopwatch = new Stopwatch();
            ProccessTimer.Interval = 1000; // Timer interval set to 1 second
            ProccessTimer.Tick += ProccessTimer_Tick;

            //settings load
            getFileAddresses();
            getDefaults();
            updateSettingsPage();
            updateOptionsForCords();

            //update page and load species
            refreshAnalysedFiles();
            loadRareSpecies();

            tableLayoutAnalysis.Controls.Add(graph1);

            this.Size = new System.Drawing.Size(440, 720);

            allGraphTypes = graphTypeSelection.Items.Cast<string>().ToList();


            graphTypeSelection.Enabled = false;
        }

        /// <summary>
        /// Represents an analysed result.
        /// </summary>
        public class AnalysisResult
        {
            public string CommonName { get; set; }
            public string ScientificName { get; set; }
            public int Occurrences { get; set; }
            public List<(double StartTime, double EndTime, double Confidence)> Timestamps { get; set; }
        }

        /// <summary>
        /// stores location name and co-ordinates
        /// </summary>
        public class Location
        {
            public string Name { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }

            public Location(string name, double latitude, double longitude)
            {
                Name = name;
                Latitude = latitude;
                Longitude = longitude;
            }
        }

        /// <summary>
        /// represents a datablock of entrys and occurences
        /// </summary>
        public class DataBlock
        {
            public string Filename { get; set; }
            public DateTime StartTimeDateHHmmss { get; set; }
            public double MinConfidence { get; set; }
            public double Overlap { get; set; }
            public double Sensitivity { get; set; }
            public string Location { get; set; }
            public string Coordinates { get; set; }
            public bool TimeOfYearUsed { get; set; }

            // Add a collection for DataEntries
            public List<DataEntry> Entries { get; set; } = new List<DataEntry>();

            // Deep copy method (if needed for cloning this object)
            public DataBlock DeepCopy()
            {
                var copy = (DataBlock)this.MemberwiseClone();
                copy.Entries = new List<DataEntry>(this.Entries);  // Copy entries list
                return copy;
            }

            // Nested class representing DataEntry from DataEntries table
            public class DataEntry
            {
                public int EntryID { get; set; }
                public BirdSpecies BirdSpecies { get; set; } // Each entry has a BirdSpecies object
                public int Occurrences { get; set; }
                public List<OccurrenceDetail> OccurrenceDetails { get; set; } = new List<OccurrenceDetail>();

                // Deep copy method for DataEntry
                public DataEntry DeepCopy()
                {
                    var copy = (DataEntry)this.MemberwiseClone();
                    copy.BirdSpecies = this.BirdSpecies?.DeepCopy(); // Copy BirdSpecies
                    copy.OccurrenceDetails = new List<OccurrenceDetail>(this.OccurrenceDetails); // Copy occurrences list
                    return copy;
                }

                // Nested class representing OccurrenceDetails from TimeStamps table
                public class OccurrenceDetail
                {
                    public int TimestampID { get; set; }
                    public int StartRange { get; set; }
                    public int EndRange { get; set; }
                    public double Confidence { get; set; }

                    // Deep copy method for OccurrenceDetail
                    public OccurrenceDetail DeepCopy()
                    {
                        return (OccurrenceDetail)this.MemberwiseClone();
                    }
                }
            }

            // Nested class representing BirdSpecies from BirdSpecies table
            public class BirdSpecies
            {
                public int BirdSpecies_Id { get; set; }
                public string CommonName { get; set; }
                public string ScientificName { get; set; }
                public bool IsRare { get; set; }  // Added IsRare
                public string RarityLevel { get; set; }  // Added Rarity

                // Deep copy method for BirdSpecies
                public BirdSpecies DeepCopy()
                {
                    return (BirdSpecies)this.MemberwiseClone();
                }
            }


        }

        /// <summary>
        /// represents rareSpecies from the rare species file
        /// </summary>
        public class rareSpecies
        {
            public string commonName { get; set; }
            public string scientificName { get; set; }
            public string rarity { get; set; }
        }



        //initialize page workflow
        private void getFileAddresses()
        {
            try
            {
                //Use AppDomain.CurrentDomain.BaseDirectory to get the directory of the running application
                string baseDirectoryResources = AppDomain.CurrentDomain.BaseDirectory;

                //string baseDirectoryResources = Directory.GetParent(Directory.GetParent(Directory.GetCurrentDirectory()).FullName)?.FullName;
                
                pythonAnalyserExeAddress = System.IO.Path.Combine(baseDirectoryResources, "BirdNetAnalyser.exe");
                BirdIDDataBase = System.IO.Path.Combine(baseDirectoryResources, "BirdID-V0.02 - Release.db");

                // Set paths for configuration files
                settingsFilePath = System.IO.Path.Combine(baseDirectoryResources, "AutomaticBirdIdConfig.txt");
                rareBirdsListFilePath = System.IO.Path.Combine(baseDirectoryResources, "rare_Species.txt");

                // Optionally, add checks to confirm that files exist
                if (!File.Exists(pythonAnalyserExeAddress))
                {
                    MessageBox.Show($"The required file '{pythonAnalyserExeAddress}' is missing.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions by displaying a message box
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void getDefaults()
        {
            if (!File.Exists(settingsFilePath))
            {
                MessageBox.Show("Settings file not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Initialize list to store locations
            savedLocations.Clear();

            try
            {
                using (var sr = new StreamReader(settingsFilePath))
                {
                    // Read all lines from the file
                    try
                    {
                        // Check if the settings file exists before attempting to read it
                        if (!File.Exists(settingsFilePath))
                        {
                            MessageBox.Show("Settings file not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }


                        // Read all lines from the file
                        string[] lines = sr.ReadToEnd().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                        if (lines.Length < 5)
                        {
                            MessageBox.Show("Settings file is missing required lines.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // Read the first four lines for settings
                        if (!double.TryParse(lines[0].Trim(), out double confidenceValueParsed))
                        {
                            MessageBox.Show("Invalid confidence value in settings file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        confidenceValue = confidenceValueParsed;

                        defaultLocation = lines[1].Trim();
                        outputFilePath = lines[2].Trim();
                        inputFilePath = lines[3].Trim();
                        birdnetPackageVersion = lines[4].Trim();

                        if (string.IsNullOrEmpty(outputFilePath) || outputFilePath.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrEmpty(inputFilePath) || inputFilePath.Equals("null", StringComparison.OrdinalIgnoreCase))
                        {
                            MessageBox.Show("Please select input and output folders in settings before use! Defaulted To Desktop please click save in settings first!",
                                            "This is an informational message.",
                                            MessageBoxButtons.OK,
                                            MessageBoxIcon.Information);
                            outputFilePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            inputFilePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                        }



                        bool isInLocationsSection = false;

                        // Iterate through remaining lines
                        for (int i = 5; i < lines.Length; i++)
                        {
                            string line = lines[i].Trim();

                            if (string.IsNullOrEmpty(line))
                            {
                                continue;
                            }

                            if (line.Equals("Locations", StringComparison.OrdinalIgnoreCase))
                            {
                                isInLocationsSection = true;
                                continue; // Skip the "Locations" header line
                            }

                            if (isInLocationsSection)
                            {
                                // Process location line
                                int lastSpaceIndex = line.LastIndexOf(' ');
                                if (lastSpaceIndex != -1)
                                {
                                    string name = line.Substring(0, lastSpaceIndex).Trim();
                                    string coordinates = line.Substring(lastSpaceIndex + 1).Trim();

                                    string[] locationData = coordinates.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

                                    if (locationData.Length >= 2)
                                    {
                                        if (double.TryParse(locationData[0], out double latitude) &&
                                            double.TryParse(locationData[1], out double longitude))
                                        {
                                            savedLocations.Add(new Location(name, latitude, longitude));
                                        }
                                        else
                                        {
                                            MessageBox.Show($"Invalid coordinate format in line: {line}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                        }
                                    }
                                    else
                                    {
                                        MessageBox.Show($"Invalid location format in line: {line}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred while reading the settings file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception occurred while loading settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }

        private void updateSettingsPage()
        {
            defaultMinConfSettingBox.Text = confidenceValue.ToString();

            defaultLocationSettingBox.Items.Clear();

            foreach (var location in savedLocations)
            {
                if (!defaultLocationSettingBox.Items.Contains(location.Name))
                {
                    defaultLocationSettingBox.Items.Add(location.Name);
                    predefinedCordsBox.Items.Add(location.Name);
                }
            }

            predefinedCordsBox.SelectedItem = defaultLocation;
            defaultLocationSettingBox.SelectedItem = defaultLocation;

            defaultOutputFolderLocationLabel.Text = outputFilePath;
            inputLocationDefaultlabel.Text = inputFilePath;

            // Multiply by 10
            double multipliedValue = confidenceValue * 100;

            // Convert to int
            int multipliedValueInt = (int)multipliedValue;

            minimumConfidenceTrackbar.Value = multipliedValueInt;
            minConfidenceNumber.Text = confidenceValue.ToString();

            birdnetVersionNumber.Text = birdnetPackageVersion;
        }

        private void updateOptionsForCords()
        {
            if (manualEntryLatLngCheckBox.Checked)
            {
                manualEntryLatLng.Enabled = true;
                manualEntryLatLng.Text = "Manual Entry (Lat,Lng):";
                latInputBox.Enabled = true;
                lngInputBox.Enabled = true;

                predefinedCordsBox.Enabled = false;
                preDefinedCordsLabel.Enabled = false;
            }
            else
            {
                manualEntryLatLng.Text = "Manual Entry OFF";
                manualEntryLatLng.Enabled = false;
                latInputBox.Enabled = false;
                lngInputBox.Enabled = false;


                predefinedCordsBox.Enabled = true;
                preDefinedCordsLabel.Enabled = true;
            }
        }

        private void UpdateSavedLocations()
        {
            // Define the line to append
            string newLocationLine = $"{savedLocations[savedLocations.Count - 1].Name} {savedLocations[savedLocations.Count - 1].Latitude},{savedLocations[savedLocations.Count - 1].Longitude}";


            try
            {
                // Use 'using' to ensure the StreamWriter is properly disposed of
                using (var sw = new StreamWriter(settingsFilePath, append: true))
                {
                    // Write the new location to the file

                    sw.WriteLine(newLocationLine);
                }
            }
            catch (IOException ex)
            {
                MessageBox.Show($"I/O Exception occurred while appending location: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception occurred while appending location: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void refreshAnalysedFiles()
        {
            analysedFilesListBox.Items.Clear();
            selectBatchComboBox.Items.Clear();

            try
            {
                // Query to get the batch details (BatchID and BatchName) from the Batches table
                string batchQuery = @"
                SELECT BatchID, BatchName 
                FROM Batches;";

                List<string> batchesToDisplay = new List<string>();
                string connectionString = $"Data Source={BirdIDDataBase};Version=3;";

                using (var connection = new SQLiteConnection(connectionString))
                using (SQLiteCommand cmd = new SQLiteCommand(batchQuery, connection))
                {
                    connection.Open(); // Open the connection if it's not already open

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        // Iterate through the results and format them for the ComboBox
                        while (reader.Read())
                        {
                            int batchID = reader.GetInt32(0);
                            string batchName = reader.GetString(1);

                            // Format the display string (display BatchID and BatchName)
                            string displayString = $"{batchName} (ID: {batchID})";

                            // Add the formatted string to the list
                            batchesToDisplay.Add(displayString);
                        }
                    }
                }

                selectBatchComboBox.Items.Add("All Files");
                // Check if any batches were found
                if (batchesToDisplay.Count > 0)
                {
                    // Add the batches to the selectBatchComboBox
                    foreach (string batch in batchesToDisplay)
                    {
                        selectBatchComboBox.Items.Add(batch);
                    }
                }
                else
                {
                    // If no batches were found in the database, display a message
                    selectBatchComboBox.Items.Add("No batches found in the database.");
                }

                // Query the database to get the file details (FileName, MinConfidence, Location, TimeOfYearUsed, Overlap, Sensitivity)
                string fileQuery = @"
                SELECT FileName, MinConfidence, Location, Coordinates, Overlap, Sensitivity, TimeOfYearUsed
                FROM AnalyzedFiles;";

                List<string> filesToDisplay = new List<string>();

                using (var connection = new SQLiteConnection(connectionString))
                using (SQLiteCommand cmd = new SQLiteCommand(fileQuery, connection))
                {
                    connection.Open(); // Open the connection if it's not already open

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        // Iterate through the results and format them for the ListBox
                        while (reader.Read())
                        {
                            string fileName = reader.GetString(0);
                            double minConfidence = reader.GetDouble(1);
                            string location = reader.IsDBNull(2) ? null : reader.GetString(2);
                            string coordinates = reader.IsDBNull(3) ? null : reader.GetString(3);
                            double overlap = reader.GetDouble(4);
                            double sensitivity = reader.GetDouble(5);
                            bool timeOfYearUsed = reader.GetBoolean(6);

                            // Determine which information to display: Location or Coordinates
                            string locationOrCoordinates = !string.IsNullOrEmpty(location) ? location : (string.IsNullOrEmpty(coordinates) ? "Not available" : coordinates);

                            // Format the display string with relevant information
                            string displayString = $"{fileName} - MinConf: {minConfidence}, Location/Coordinates: {locationOrCoordinates}, " +
                                $"Overlap: {overlap}, Sensitivity: {sensitivity}, TimeOfYearUsed: {timeOfYearUsed}";

                            // Add the formatted string to the list
                            filesToDisplay.Add(displayString);
                        }
                    }
                }

                // Check if any files were found
                if (filesToDisplay.Count > 0)
                {
                    // Add the files to both the ListBox and ComboBox
                    foreach (string file in filesToDisplay)
                    {
                        analysedFilesListBox.Items.Add(file);
                        selectFileComboBox.Items.Add(file);
                    }
                }
                else
                {
                    // If no files were found in the database, display a message
                    analysedFilesListBox.Items.Add("No analyzed files found in the database.");
                }
            }
            catch (Exception ex)
            {
                // Show an error message if something goes wrong
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void loadRareSpecies()
        {
            foreach (var line in File.ReadLines(rareBirdsListFilePath))
            {
                var parts = line.Split('.');

                var rarityCode = parts[2].TrimEnd(' ', ',').ToUpper()[0].ToString();
                string rarityDescription;

                // Use switch to map rarity codes to descriptions
                switch (rarityCode)
                {
                    case "M":
                        rarityDescription = "Mega Rarity";
                        break;
                    case "R":
                        rarityDescription = "Rarity";
                        break;
                    case "S":
                        rarityDescription = "Scarcity";
                        break;
                    case "U":
                        rarityDescription = "Uncommon";
                        break;
                    default:
                        rarityDescription = "Unknown";
                        break;
                }
                if (parts.Length == 3)
                {
                    var species = new rareSpecies
                    {
                        commonName = parts[0].Trim(),
                        scientificName = parts[1].Trim(),
                        rarity = rarityDescription
                    };
                    rareSpeciesList.Add(species);
                }
            }
        }



       

       
        //File proccessing updates
        private void fileProccessingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(440, 740);
            this.Size = new System.Drawing.Size(440, 740);
            this.MaximumSize = new System.Drawing.Size(0, 0);
            fileProccessingPanel.Visible = true;
            settingsPanel.Visible = false;
            analysisPanel.Visible = false;
            ExportPanel.Visible = false;
            speciesMetricsPanel.Visible = false;
        }

        //selecting removing files
        /// <summary>
        /// Opens a file dialog to select audio files and updates the selected files list.
        /// </summary>
        private void selectFileButton_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                // Filter to allow only audio files and all files.
                openFileDialog.Filter = "Audio Files (*.wav;*.mp3)|*.wav;*.mp3|All Files (*.*)|*.*";
                openFileDialog.Title = "Select Audio File(s)";
                openFileDialog.Multiselect = true; // Enable multiple file selection

                // Show the dialog and check if the user selected files.
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    // Iterate through all selected files and add them to the list.
                    foreach (var selectedFilePath in openFileDialog.FileNames)
                    {
                        selectedFilePathsFullList.Add(selectedFilePath);
                    }
                    // Update the display list with the selected file paths.
                    updateSelectedFilesList();
                }
            }
        }

        /// <summary>
        /// Updates the list of selected files displayed in the UI.
        /// </summary>
        private void updateSelectedFilesList()
        {
            selectedFilesListBox.Items.Clear();

            if (selectedFilePathsFullList.Count <= 0)
            {
                selectedFilesListBox.Items.Add("No Files Selected!");
            }

            foreach (var file in selectedFilePathsFullList)
            {
                selectedFilesListBox.Items.Add(System.IO.Path.GetFileName(file));
            }
        }

        private void removeSelectedFiles_Click(object sender, EventArgs e)
        {
            List<string> itemToRemove = new List<string>();
            foreach (var i in selectedFilesListBox.SelectedItems)
            {
                itemToRemove.Add(i.ToString());
            }

            foreach (var i in itemToRemove)
            {
                selectedFilesListBox.Items.Remove(i);
                selectedFilePathsFullList.RemoveAll(path => path.EndsWith($"\\{i}", StringComparison.OrdinalIgnoreCase));
            }

            if (selectedFilesListBox.Items.Count == 0)
            {
                selectedFilesListBox.Items.Add("No Files Selected!");
            }
        }

        //handling sliders
        private void minimumConfidenceTrackbar_Scroll(object sender, EventArgs e)
        {
            updateConfidence();
        }
        private void updateConfidence()
        {
            if (minimumConfidenceTrackbar.Value != 100)
            {
                if (minimumConfidenceTrackbar.Value < 10)
                {
                    minConfidenceNumber.Text = "0.0" + minimumConfidenceTrackbar.Value;
                    confidenceValue = double.Parse(minConfidenceNumber.Text);
                }
                else
                {
                    minConfidenceNumber.Text = "0." + minimumConfidenceTrackbar.Value;
                    confidenceValue = double.Parse(minConfidenceNumber.Text);
                }
            }
            else
            {
                minConfidenceNumber.Text = "1.00";
                confidenceValue = 1;
            }
        }

        private void sensitvityAmountSlider_Scroll(object sender, EventArgs e)
        {
            updateSensitvitySlider();
        }
        private void updateSensitvitySlider()
        {
            sensitvityAmountLabel.Text = (double.Parse(sensitvityAmountSlider.Value.ToString()) / 10).ToString();
            if (sensitvityAmountLabel.Text == "1")
            {
                sensitvityAmountLabel.Text = "1.0";
            }
            sensitivity = double.Parse(sensitvityAmountLabel.Text);
        }

        private void overlapAmountSlider_Scroll(object sender, EventArgs e)
        {
            updateOverlapSlider();
        }
        private void updateOverlapSlider()
        {
            overlapAmountLabel.Text = (double.Parse(overlapAmountSlider.Value.ToString()) / 10).ToString();
            if (overlapAmountLabel.Text.Count() == 1)
            {
                overlapAmountLabel.Text += ".0";
            }
            Overlap = double.Parse(overlapAmountLabel.Text);
        }


        //handling species filters
        private void predefinedCordsBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            currentLocationSelection = predefinedCordsBox.SelectedItem.ToString();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            updateOptionsForCords();
        }

        private void byTimeOfYearButton_Click(object sender, EventArgs e)
        {
            if (byTimeOfYearButton.Text == "OFF")
            {
                byTimeOfYearButton.Text = "ON";
                useByTimeOfYearSpecies = true;
            }
            else
            {
                byTimeOfYearButton.Text = "OFF";
                useByTimeOfYearSpecies = false;
            }
        }

        //start and pgogress bar
        /// <summary>
        /// Starts the analysis for each selected file and processes them concurrently.
        /// </summary>
        private void startAnalysisButton_Click(object sender, EventArgs e)
        {
            startAnalysis();
        }
        private async void startAnalysis()
        {
            disableItems();


            if (selectedFilePathsFullList.Count == 0)
            {

                MessageBox.Show("No Files Selected, Please Select A File First", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                enableItems();

                return;
            }


            double lat = 0.0000;
            double lng = 0.0000;

            if (manualEntryLatLngCheckBox.Checked)
            {
                lat = double.Parse(latInputBox.Text);
                lng = double.Parse(lngInputBox.Text);
            }
            else
            {
                var selectedLocation = savedLocations.FirstOrDefault(location => location.Name == currentLocationSelection);

                if (selectedLocation != null)
                {
                    latitude = selectedLocation.Latitude;
                    longitude = selectedLocation.Longitude;
                }
                else
                {
                    // Handle the case where the location is not found
                    MessageBox.Show("Location not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                lat = latitude;
                lng = longitude;
            }


            _stopwatch.Restart();
            ProccessTimer.Start();

            updateProgressBar();

            // List to hold all the tasks
            var tasks = new List<Task>();

            // Semaphore to limit concurrency to 3
            using (var semaphore = new SemaphoreSlim(3))
            {
                // Process all selected files with concurrency control
                foreach (var filePath in selectedFilePathsFullList)
                {
                    DateTime formattedDateTime = getFormatedDatetimeFromfileName(filePath);

                    string formattedDate = formattedDateTime.ToString("yyyy-MM-dd");

                    string arguments = useByTimeOfYearSpecies
                        ? $"\"{filePath}\" {lat} {lng} {formattedDate} {confidenceValue} {Overlap} {sensitivity}"
                        : $"\"{filePath}\" {lat} {lng} {confidenceValue} {Overlap} {sensitivity}";

                    startAnalysisButton.Enabled = false;

                    // Create and start a task for each file processing
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(); // Wait for an available slot
                        try
                        {
                            await RunPythonScrip(arguments, System.IO.Path.GetFileName(filePath));
                        }
                        finally
                        {
                            semaphore.Release(); // Release the slot
                        }
                    }));
                }

                // Await all the tasks to complete
                await Task.WhenAll(tasks);
            }

            _stopwatch.Stop();
            ProccessTimer.Stop();
            startAnalysisButton.Enabled = true;

            // Save all results to Excel
            await NewBatchSaveToDatabase();
            //await SaveAllResultsToExcelAsync();
            analysisProgressBar.Value = analysisProgressBar.Maximum;

            _allResults.Clear();

            refreshAnalysedFiles();
            MessageBox.Show("File Proccesign Complete!", "Upload Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            enableItems();
            analysisProgressBar.Value = 0;
        }

        private DateTime getFormatedDatetimeFromfileName(string fileName)
        {
            string[] splitFileName = fileName.Split('_');
            string dateTime = splitFileName[1] + " " + splitFileName[2].Split('.')[0];

            DateTime parsedDate = DateTime.ParseExact(dateTime, "yyyyMMdd HHmmss", null);

            return parsedDate;
        }

        private void updateProgressBar()
        {
            analysisProgressBar.Value += 10;
        }

        /// <summary>
        /// Updates the elapsed time label in the UI with the current stopwatch time.
        /// </summary>
        private void UpdateElapsedTimeLabel()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateElapsedTimeLabel));
            }
            else
            {
                timerLabelDisplay.Text = $"{_stopwatch.Elapsed.ToString(@"hh\:mm\:ss")}";

                if (_stopwatch.Elapsed.TotalSeconds > lastTimeCheck + 10 && analysisProgressBar.Value < 90)
                {
                    updateProgressBar();
                    lastTimeCheck += 10;
                }
                else if (_stopwatch.Elapsed.TotalSeconds > lastTimeCheck + 10 && analysisProgressBar.Value >= 90)
                {
                    lastTimeCheck += 10;
                    analysisProgressBar.Maximum += 10;
                    analysisProgressBar.Value += 10;
                }
            }
        }

        /// <summary>
        /// blocks user access to features whilst proccessing.
        /// </summary>
        private void disableItems()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(disableItems));
            }
            else
            {
                foreach (System.Windows.Forms.Control control in fileProccessingPanel.Controls)
                {
                    if (control.Name.Contains("Slider") || control.Name.Contains("Trackbar") || control.Name.Contains("Button") || control.Name.Contains("removeSelectedFiles"))
                    {
                        control.Enabled = false;
                    }
                }
            }
        }
        private void enableItems()
        {

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(enableItems));
            }
            else
            {
                foreach (System.Windows.Forms.Control control in fileProccessingPanel.Controls)
                {
                    if (control.Name.Contains("Slider") || control.Name.Contains("Trackbar") || control.Name.Contains("Button") || control.Name.Contains("removeSelectedFiles"))
                    {
                        control.Enabled = true;
                    }
                }
            }

        }

        //python 
        /// <summary>
        /// Runs the Python script with the given arguments and processes the output.
        /// </summary>
        private async Task RunPythonScrip(string arguments, string fileName)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = pythonAnalyserExeAddress,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using (Process process = Process.Start(start))
                {
                    Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

                    // Wait for the process to exit
                    await Task.Run(() => process.WaitForExit());

                    string result = await standardOutputTask;
                    string error = await standardErrorTask;

                    MessageBox.Show(error + " result: " + result);

                    if (!string.IsNullOrEmpty(error) && !error.Contains("Couldn't find ffmpeg or avconv - defaulting to ffmpeg"))
                    {
                        MessageBox.Show($"Error: {error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }





                    ExtractJsonInfo(result, fileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception: {ex.Message}", "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        //json handling
        /// <summary>
        /// Extracts JSON info from the output and saves the results.
        /// </summary>
        private void ExtractJsonInfo(string output, string fileName)
        {
            // Split the output into lines
            string[] lines = output.Split(new[] { "\n" }, StringSplitOptions.None);

            // Check if we have more than 14 lines and return the lines after the header
            if (lines.Length > 10)
            {
                // Join all lines into a single string
                string allLines = string.Join("\n", lines);

                // Find the index of the first occurrence of '{'
                int braceIndex = allLines.IndexOf('{');

                // If an opening brace is found
                if (braceIndex != -1)
                {
                    /// Extract the JSON part starting from the first '{'
                    string jsonPart = allLines.Substring(braceIndex);

                    // Check if the JSON part contains version info at the start
                    var versionInfo = (JObject.Parse(jsonPart));
                    string version = versionInfo["birdnetlib_version"]?.ToString();
                    birdnetPackageVersion = version;
                    birdnetVersionNumber.Text = birdnetPackageVersion;
                    // Parse the remaining JSON data for detections
                    var results = ParseJsonResults(jsonPart);
                    SaveResults(results, fileName);
                }
                else
                {
                    // Handle the case where no opening brace is found
                    throw new InvalidOperationException("No opening brace '{' found in the input.");
                }
            }
            else
            {
                // Handle the case where there are not enough lines
                throw new Exception("Output does not contain enough lines to remove header.");
            }
        }

        /// Parses the JSON results and creates a list of AnalysisResult objects.
        /// </summary>
        /// <param name="json">The JSON string to parse.</param>
        /// <returns>A list of AnalysisResult objects.</returns>
        private List<AnalysisResult> ParseJsonResults(string json)
        {
            var results = new List<AnalysisResult>();
            var jsonObject = JObject.Parse(json);

            foreach (var item in jsonObject["detections"])
            {
                var commonName = item["common_name"].ToString();
                var scientificName = item["scientific_name"].ToString();
                var startTime = (double)item["start_time"];
                var endTime = (double)item["end_time"];
                var confidence = (double)item["confidence"];

                var existingResult = results.FirstOrDefault(r => r.ScientificName == scientificName && r.CommonName == commonName);

                if (existingResult == null)
                {
                    results.Add(new AnalysisResult
                    {
                        CommonName = commonName,
                        ScientificName = scientificName,
                        Occurrences = 1,
                        Timestamps = new List<(double, double, double)> { (startTime, endTime, confidence) }
                    });
                }
                else
                {
                    existingResult.Occurrences++;
                    existingResult.Timestamps.Add((startTime, endTime, confidence));
                }
            }

            return results;
        }

        //saving json
        /// <summary>
        /// Saves the results for a specific file.
        /// </summary>
        private void SaveResults(List<AnalysisResult> results, string fileName)
        {
            lock (_resultsLock)
            {
                if (!_allResults.ContainsKey(fileName))
                {
                    _allResults[fileName] = new List<AnalysisResult>();
                }

                foreach (var result in results)
                {
                    var existingResult = _allResults[fileName].FirstOrDefault(r => r.ScientificName == result.ScientificName);
                    if (existingResult == null)
                    {
                        _allResults[fileName].Add(new AnalysisResult
                        {
                            CommonName = result.CommonName,
                            ScientificName = result.ScientificName,
                            Occurrences = result.Occurrences,
                            Timestamps = new List<(double, double, double)>(result.Timestamps)
                        });
                    }
                    else
                    {
                        existingResult.Occurrences += result.Occurrences;
                        existingResult.Timestamps.AddRange(result.Timestamps);
                    }
                }
            }
        }


        //uploading to db workflow
        private async Task NewBatchSaveToDatabase()
        {
            await Task.Run(() =>
            {
                int amountOfFiles = _allResults.Count();

                string fileTimeStamp = DateTime.Now.ToString("yyyy-MM-dd__HH-mm-ss");
                string batchName = $"Results_{fileTimeStamp}_Files:{amountOfFiles}";
                string connectionString = $"Data Source={BirdIDDataBase};Version=3;";

                try
                {
                    using (var connection = new SQLiteConnection(connectionString))
                    {
                        connection.Open();

                        int batchID = InsertBatch(connection, batchName);

                        foreach (var fileName in _allResults)
                        {
                            string location = "";
                            string coordinates = "";

                            if (predefinedCordsBox.Enabled == false)
                            {
                                coordinates = latitude.ToString() + longitude.ToString();
                            }
                            else
                            {
                                location = currentLocationSelection;
                            }

                            int fileId = InsertOrGetFileID(connection, fileName.Key, confidenceValue, Overlap, sensitivity, location, coordinates, useByTimeOfYearSpecies);
                            LinkBatchToFile(connection, batchID, fileId);
                            var entries = fileName.Value;

                            foreach (var entry in entries)
                            {
                                int entryID = InsertBirdDataEntry(connection, fileId, entry, rareSpeciesList);

                                foreach (var timestamp in entry.Timestamps)
                                {
                                    InsertTimestamp(connection, entryID, timestamp);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { };
            });
        }

        private static int InsertBatch(SQLiteConnection connection, string batchName)
        {
            string insertBatchQuery = "INSERT INTO Batches (BatchName) VALUES (@batchName)";

            using (SQLiteCommand cmd = new SQLiteCommand(insertBatchQuery, connection))
            {
                cmd.Parameters.AddWithValue("@batchName", batchName);
                cmd.ExecuteNonQuery();
            }

            string getBatchIdQuery = "SELECT BatchID FROM Batches WHERE BatchName = @BatchName;";
            using (SQLiteCommand cmd = new SQLiteCommand(getBatchIdQuery, connection))
            {
                cmd.Parameters.AddWithValue("@BatchName", batchName);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static int InsertOrGetFileID(SQLiteConnection connection, string fileName, double minConf, double overlap, double sensitivity, string location, string coordinates, bool timeOfYearUsed)
        {
            string[] splitFileName = System.IO.Path.GetFileName(fileName).Split('_');
            string date = splitFileName[1];
            string[] time = splitFileName[2].Split('.');

            string datetime = date + " " + time[0];
            DateTime formattedTime = DateTime.ParseExact(datetime, "yyyyMMdd HHmmss", CultureInfo.InvariantCulture);

            // Step 1: Check for any matches on the specified attributes
            string checkFileQuery = @"
            SELECT COUNT(*) FROM AnalyzedFiles 
            WHERE FileName = @FileName 
            AND MinConfidence = @MinConfidence 
            AND Overlap = @Overlap 
            AND Sensitivity = @Sensitivity 
            AND Location = @Location 
            AND Coordinates = @Coordinates 
            AND TimeOfYearUsed = @TimeOfYearUsed;";

            using (SQLiteCommand checkCmd = new SQLiteCommand(checkFileQuery, connection))
            {
                checkCmd.Parameters.AddWithValue("@FileName", fileName);
                checkCmd.Parameters.AddWithValue("@MinConfidence", minConf);
                checkCmd.Parameters.AddWithValue("@Overlap", overlap);
                checkCmd.Parameters.AddWithValue("@Sensitivity", sensitivity);
                checkCmd.Parameters.AddWithValue("@Location", location);
                checkCmd.Parameters.AddWithValue("@Coordinates", coordinates);
                checkCmd.Parameters.AddWithValue("@TimeOfYearUsed", timeOfYearUsed);

                // Execute the query to check for matches
                int matchCount = Convert.ToInt32(checkCmd.ExecuteScalar());

                if (matchCount == 0)
                {
                    // Step 2: Insert only if no matches are found
                    string insertFileQuery = @"
                    INSERT INTO AnalyzedFiles 
                    (FileName, StartDateTime, MinConfidence, Overlap, Sensitivity, Location, Coordinates, TimeOfYearUsed) 
                    VALUES 
                    (@FileName, @StartDateTime, @MinConfidence, @Overlap, @Sensitivity, @Location, @Coordinates, @TimeOfYearUsed);";

                    using (SQLiteCommand insertCmd = new SQLiteCommand(insertFileQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@FileName", fileName);
                        insertCmd.Parameters.AddWithValue("@StartDateTime", formattedTime);
                        insertCmd.Parameters.AddWithValue("@MinConfidence", minConf);
                        insertCmd.Parameters.AddWithValue("@Overlap", overlap);
                        insertCmd.Parameters.AddWithValue("@Sensitivity", sensitivity);
                        insertCmd.Parameters.AddWithValue("@Location", location);
                        insertCmd.Parameters.AddWithValue("@Coordinates", coordinates);
                        insertCmd.Parameters.AddWithValue("@TimeOfYearUsed", timeOfYearUsed);
                        insertCmd.ExecuteNonQuery();
                    }
                }
            }

            // Step 3: Retrieve the FileID of the inserted or matching file
            string getFileIdQuery = @"
            SELECT FileID 
            FROM AnalyzedFiles 
            WHERE FileName = @FileName 
            AND MinConfidence = @MinConfidence 
            AND Overlap = @Overlap 
            AND Sensitivity = @Sensitivity 
            AND Location = @Location 
            AND Coordinates = @Coordinates 
            AND TimeOfYearUsed = @TimeOfYearUsed;";

            using (SQLiteCommand cmd = new SQLiteCommand(getFileIdQuery, connection))
            {
                cmd.Parameters.AddWithValue("@FileName", fileName);
                cmd.Parameters.AddWithValue("@MinConfidence", minConf);
                cmd.Parameters.AddWithValue("@Overlap", overlap);
                cmd.Parameters.AddWithValue("@Sensitivity", sensitivity);
                cmd.Parameters.AddWithValue("@Location", location);
                cmd.Parameters.AddWithValue("@Coordinates", coordinates);
                cmd.Parameters.AddWithValue("@TimeOfYearUsed", timeOfYearUsed);

                object result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : -1; // Return -1 if no match is found
            }
        }

        private static void LinkBatchToFile(SQLiteConnection connection, int batchId, int fileId)
        {
            string linkQuery = "INSERT OR IGNORE INTO BatchFiles (BatchID, FileID) VALUES (@BatchID, @FileID);";
            using (SQLiteCommand cmd = new SQLiteCommand(linkQuery, connection))
            {
                cmd.Parameters.AddWithValue("@BatchID", batchId);
                cmd.Parameters.AddWithValue("@FileID", fileId);
                cmd.ExecuteNonQuery();
            }
        }

        private static int InsertBirdDataEntry(SQLiteConnection connection, int fileId, AnalysisResult entry, List<rareSpecies> rareSpeciesList)
        {
            // Determine if the bird is rare based on the rareSpeciesList
            bool isRareCheck = false;
            string rarityLevelCheck = "Common";

            if (rareSpeciesList.Any(species => species.commonName.Equals(entry.CommonName, StringComparison.OrdinalIgnoreCase)))
            {
                if (rareSpeciesList.Any(species => species.scientificName.Equals(entry.ScientificName, StringComparison.OrdinalIgnoreCase)))
                {
                    var speciesThatIsRare = rareSpeciesList
                        .FirstOrDefault(s => s.commonName.Equals(entry.CommonName, StringComparison.OrdinalIgnoreCase));

                    isRareCheck = true;
                    rarityLevelCheck = speciesThatIsRare?.rarity ?? "Unknown";
                }
            }


            // Check if the bird species already exists in the BirdSpecies table
            string getSpeciesIdQuery = @"
            SELECT BirdSpecies_Id 
            FROM BirdSpecies 
            WHERE CommonName = @CommonName AND ScientificName = @ScientificName;
            ";

            int birdSpeciesId;
            using (SQLiteCommand cmd = new SQLiteCommand(getSpeciesIdQuery, connection))
            {
                cmd.Parameters.AddWithValue("@CommonName", entry.CommonName);
                cmd.Parameters.AddWithValue("@ScientificName", entry.ScientificName);

                object result = cmd.ExecuteScalar();
                if (result != null)
                {
                    birdSpeciesId = Convert.ToInt32(result);
                }
                else
                {
                    // Insert the bird species into the BirdSpecies table
                    string insertSpeciesQuery = @"
                    INSERT INTO BirdSpecies (CommonName, ScientificName, IsRare, Rarity) 
                    VALUES (@CommonName, @ScientificName, @IsRare, @Rarity);
                    ";

                    using (SQLiteCommand insertCmd = new SQLiteCommand(insertSpeciesQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@CommonName", entry.CommonName);
                        insertCmd.Parameters.AddWithValue("@ScientificName", entry.ScientificName);
                        insertCmd.Parameters.AddWithValue("@IsRare", isRareCheck);
                        insertCmd.Parameters.AddWithValue("@Rarity", rarityLevelCheck);
                        insertCmd.ExecuteNonQuery();
                    }

                    // Get the newly inserted BirdSpecies_Id
                    birdSpeciesId = (int)connection.LastInsertRowId;
                }
            }

            // Insert the data entry into the DataEntries table
            string insertEntryQuery = @"
            INSERT INTO DataEntries (FileID, BirdSpecies_Id, Occurrences) 
            VALUES (@FileID, @BirdSpecies_Id, @Occurrences);
            ";

            int tempOC = entry.Occurrences;



            using (SQLiteCommand cmd = new SQLiteCommand(insertEntryQuery, connection))
            {
                cmd.Parameters.AddWithValue("@FileID", fileId);
                cmd.Parameters.AddWithValue("@BirdSpecies_Id", birdSpeciesId);
                cmd.Parameters.AddWithValue("@Occurrences", tempOC);
                cmd.ExecuteNonQuery();
            }

            // Retrieve the EntryID of the inserted data entry
            string getEntryIdQuery = @"
            SELECT EntryID 
            FROM DataEntries 
            WHERE FileID = @FileID AND BirdSpecies_Id = @BirdSpecies_Id;
            ";

            using (SQLiteCommand cmd = new SQLiteCommand(getEntryIdQuery, connection))
            {
                cmd.Parameters.AddWithValue("@FileID", fileId);
                cmd.Parameters.AddWithValue("@BirdSpecies_Id", birdSpeciesId);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static void InsertTimestamp(SQLiteConnection connection, int entryId, (double StartTime, double EndTime, double Confidence) timestamp)
        {
            string insertTimestampQuery = @"
            INSERT INTO TimeStamps 
            (EntryID, StartRange, EndRange, Confidence) 
            VALUES 
            (@EntryID, @StartRange, @EndRange, @Confidence);
            ";

            using (SQLiteCommand cmd = new SQLiteCommand(insertTimestampQuery, connection))
            {
                cmd.Parameters.AddWithValue("@EntryID", entryId);
                cmd.Parameters.AddWithValue("@StartRange", timestamp.StartTime);
                cmd.Parameters.AddWithValue("@EndRange", timestamp.EndTime);
                cmd.Parameters.AddWithValue("@Confidence", timestamp.Confidence);
                cmd.ExecuteNonQuery();
            }
        }






        //Graphing
        private void analysisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.MaximizeBox = true;
            fileProccessingPanel.Visible = false;
            settingsPanel.Visible = false;
            ExportPanel.Visible = false;
            speciesMetricsPanel.Visible = false;

            this.MinimumSize = new System.Drawing.Size(440, 740);
            this.MaximumSize = new System.Drawing.Size(0, 0);
            this.Size = new System.Drawing.Size(1000, 740);



            // original 590, 760
            // Calculate new position for the form to center it
            int x = (Screen.PrimaryScreen.WorkingArea.Width - this.Width) / 2;
            int y = (Screen.PrimaryScreen.WorkingArea.Height - this.Height) / 2;

            this.SetDesktopLocation(x, y); // Set the new location

            analysisPanel.Visible = true;
        }

        //selectBatch workflow
        private void selectBatchOrFileButton_Click(object sender, EventArgs e)
        {
            if (selectBatchOrFileButton.Text == "File")
            {
                selectBatchOrFileButton.Text = "Batch";

                selectBatchComboBox.Enabled = true;
                selectFileComboBox.Enabled = false;
            }
            else
            {
                selectBatchOrFileButton.Text = "File";

                selectBatchComboBox.Enabled = false;
                selectFileComboBox.Enabled = true;
            }
        }

        private void selectBatchComboBox_SelectionChangeCommitted(object sender, EventArgs e)
        {
            liveDatablocks.Clear();
            string batchName = selectBatchComboBox.Text;

            if (selectBatchComboBox.SelectedItem.ToString() != "All Files")
            {
                loadBatchIntoDatablocks(batchName);
            }
            else
            {
                loadAllFilesIntoDataSet();
            }


            loadSpeciesBox();
            graphTypeSelection.Enabled = true;
            graphTypeSelection.SelectedIndex = 0;
            selectSpeciesComboBox.SelectedIndex = 0;
            displayOnGraphUpdateTable(graphTypeSelection.SelectedIndex, selectSpeciesComboBox.SelectedItem.ToString());
        }

        private void selectFileComboBox_SelectionChangeCommitted(object sender, EventArgs e)
        {
            string fileName = selectFileComboBox.Text;
            liveDatablocks.Clear();


            loadSingleFileIntoDatablocks(fileName);
            graphTypeSelection.Enabled = true;
            liveDatablocks.Add(mainDataBlock);
            loadSpeciesBox();
            graphTypeSelection.SelectedIndex = 0;
            selectSpeciesComboBox.SelectedIndex = 0;
            displayOnGraphUpdateTable(graphTypeSelection.SelectedIndex, selectSpeciesComboBox.SelectedItem.ToString());

            //old unused
            //updateDataBlocksWithSelectedFiles();
            //displayOnGraphUpdateTable(selectSpeciesComboBox.SelectedIndex);
        }

        //load datablocks
        private void loadSingleFileIntoDatablocks(string fileNameAndAttributes)
        {
            try
            {
                // Use regex to extract the components from the displayString
                var regex = new System.Text.RegularExpressions.Regex(
                    @"^(?<FileName>.+?) - MinConf: (?<MinConfidence>[\d.]+), Location/Coordinates: (?<LocationOrCoordinates>.+?), " +
                    @"Overlap: (?<Overlap>[\d.]+), Sensitivity: (?<Sensitivity>[\d.]+), TimeOfYearUsed: (?<TimeOfYearUsed>(True|False))$");

                var match = regex.Match(fileNameAndAttributes);

                if (!match.Success)
                {
                    MessageBox.Show("Invalid display string format.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Extract the values from the regex match
                string fileName = match.Groups["FileName"].Value;
                double minConfidence = Convert.ToDouble(match.Groups["MinConfidence"].Value);
                string locationOrCoordinates = match.Groups["LocationOrCoordinates"].Value;
                double overlap = Convert.ToDouble(match.Groups["Overlap"].Value);
                double sensitivity = Convert.ToDouble(match.Groups["Sensitivity"].Value);
                bool timeOfYearUsed = match.Groups["TimeOfYearUsed"].Value.Equals("True", StringComparison.OrdinalIgnoreCase);

                // Create a DataBlock object to hold the data
                DataBlock dataBlock = new DataBlock();

                // Query the database to retrieve the file data using the extracted parameters
                string query = @"
                SELECT FileName, MinConfidence, Overlap, Sensitivity, Location, Coordinates, TimeOfYearUsed
                FROM AnalyzedFiles
                WHERE FileName = @FileName
                AND MinConfidence = @MinConfidence
                AND Overlap = @Overlap
                AND Sensitivity = @Sensitivity
                AND (Location = @LocationOrCoordinates OR Coordinates = @LocationOrCoordinates)
                AND TimeOfYearUsed = @TimeOfYearUsed;";

                string connectionString = $"Data Source={BirdIDDataBase};Version=3;";

                using (var connection = new SQLiteConnection(connectionString))
                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    // Add parameters to the query
                    cmd.Parameters.AddWithValue("@FileName", fileName);
                    cmd.Parameters.AddWithValue("@MinConfidence", minConfidence);
                    cmd.Parameters.AddWithValue("@Overlap", overlap);
                    cmd.Parameters.AddWithValue("@Sensitivity", sensitivity);
                    cmd.Parameters.AddWithValue("@LocationOrCoordinates", locationOrCoordinates);
                    cmd.Parameters.AddWithValue("@TimeOfYearUsed", timeOfYearUsed);

                    connection.Open(); // Open the connection

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // If a matching file is found, load it into the DataBlock
                            dataBlock.Filename = reader.GetString(0);
                            dataBlock.StartTimeDateHHmmss = getFormatedDatetimeFromfileName(reader.GetString(0));
                            dataBlock.MinConfidence = reader.GetDouble(1);
                            dataBlock.Overlap = reader.GetDouble(2);
                            dataBlock.Sensitivity = reader.GetDouble(3);
                            dataBlock.Location = reader.IsDBNull(4) ? "" : reader.GetString(4);
                            dataBlock.Coordinates = reader.IsDBNull(5) ? "" : reader.GetString(5);
                            dataBlock.TimeOfYearUsed = reader.GetBoolean(6);
                        }
                        else
                        {
                            MessageBox.Show("No matching file found in the database.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }

                // Now retrieve DataEntries associated with this file with exact match on file attributes
                string dataEntryQuery = @"
                SELECT DE.EntryID, DE.BirdSpecies_Id, DE.Occurrences, BS.CommonName, BS.ScientificName, BS.Rarity, BS.IsRare
                FROM DataEntries DE
                JOIN BirdSpecies BS ON DE.BirdSpecies_Id = BS.BirdSpecies_Id
                WHERE DE.FileID = (SELECT FileID FROM AnalyzedFiles WHERE FileName = @FileName
                       AND MinConfidence = @MinConfidence
                       AND Overlap = @Overlap
                       AND Sensitivity = @Sensitivity
                       AND (Location = @LocationOrCoordinates OR Coordinates = @LocationOrCoordinates)
                       AND TimeOfYearUsed = @TimeOfYearUsed);";

                using (var connection = new SQLiteConnection(connectionString))
                using (SQLiteCommand cmd = new SQLiteCommand(dataEntryQuery, connection))
                {
                    // Add parameters for the fileName and other parameters to the data entry query
                    cmd.Parameters.AddWithValue("@FileName", dataBlock.Filename);
                    cmd.Parameters.AddWithValue("@MinConfidence", dataBlock.MinConfidence);
                    cmd.Parameters.AddWithValue("@Overlap", dataBlock.Overlap);
                    cmd.Parameters.AddWithValue("@Sensitivity", dataBlock.Sensitivity);
                    cmd.Parameters.AddWithValue("@LocationOrCoordinates", dataBlock.Location == "" ? dataBlock.Coordinates : dataBlock.Location);
                    cmd.Parameters.AddWithValue("@TimeOfYearUsed", dataBlock.TimeOfYearUsed);

                    connection.Open(); // Open the connection

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // Create a new DataEntry object for each record
                            var dataEntry = new DataBlock.DataEntry
                            {
                                EntryID = reader.GetInt32(0),
                                Occurrences = reader.GetInt32(2),
                                BirdSpecies = new BirdSpecies
                                {
                                    BirdSpecies_Id = reader.GetInt32(1),
                                    CommonName = reader.GetString(3),
                                    ScientificName = reader.GetString(4),
                                    RarityLevel = reader.GetString(5),
                                    IsRare = reader.GetBoolean(6),
                                }
                            };

                            // Add the data entry to the DataBlock
                            dataBlock.Entries.Add(dataEntry);
                        }
                    }
                }

                // Now retrieve OccurrenceDetails for each DataEntry (timestamps) associated with the DataEntries
                foreach (var dataEntry in dataBlock.Entries)
                {
                    string occurrenceDetailsQuery = @"
                    SELECT TS.TimestampID, TS.StartRange, TS.EndRange, TS.Confidence
                    FROM TimeStamps TS
                    WHERE TS.EntryID = @EntryID;";

                    using (var connection = new SQLiteConnection(connectionString))
                    using (SQLiteCommand cmd = new SQLiteCommand(occurrenceDetailsQuery, connection))
                    {
                        // Add parameter for EntryID to the occurrence details query
                        cmd.Parameters.AddWithValue("@EntryID", dataEntry.EntryID);

                        connection.Open(); // Open the connection

                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Create an OccurrenceDetail object for each timestamp
                                var occurrenceDetail = new DataBlock.DataEntry.OccurrenceDetail
                                {
                                    TimestampID = reader.GetInt32(0),
                                    StartRange = reader.GetInt32(1),
                                    EndRange = reader.GetInt32(2),
                                    Confidence = reader.GetDouble(3)
                                };

                                // Add the occurrence detail to the DataEntry
                                dataEntry.OccurrenceDetails.Add(occurrenceDetail);
                            }
                        }
                    }
                }

                mainDataBlock = dataBlock;
                // Now the dataBlock contains DataEntries and their OccurrenceDetails
                // You can use the dataBlock now, for example, to display or graph the data
                // Example: Update UI elements or call a method to graph the data
                // graphData(dataBlock);

            }
            catch (Exception ex)
            {
                // Handle any errors
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void loadBatchIntoDatablocks(string batchName)
        {
            //step 1 retrieve all files linked to batch

            try
            {

                var regex = new Regex(@"^(?<BatchName>.+?)\s\(ID:\s\d+\)$");
                var match = regex.Match(batchName);

                batchName = match.Groups["BatchName"].Value;

                List<DataBlock> batchDataBlocks = new List<DataBlock>();

                string connectionString = $"Data Source={BirdIDDataBase};Version=3;";

                // Step 1: Retrieve all files linked to the batch
                string batchFilesQuery = @"
                SELECT AF.FileName, AF.MinConfidence, AF.Overlap, AF.Sensitivity, 
                AF.Location, AF.Coordinates, AF.TimeOfYearUsed, AF.StartDateTime
                FROM AnalyzedFiles AF
                JOIN BatchFiles BF ON AF.FileID = BF.FileID
                JOIN Batches B ON BF.BatchID = B.BatchID
                WHERE B.BatchName = @BatchName;";


                using (var connection = new SQLiteConnection(connectionString))
                using (var cmd = new SQLiteCommand(batchFilesQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@BatchName", batchName);
                    connection.Open();

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // Step 2: Create DataBlock for each file
                            var dataBlock = new DataBlock
                            {
                                Filename = reader.GetString(0),
                                MinConfidence = reader.GetDouble(1),
                                Overlap = reader.GetDouble(2),
                                Sensitivity = reader.GetDouble(3),
                                Location = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                Coordinates = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                TimeOfYearUsed = reader.GetBoolean(6),
                                StartTimeDateHHmmss = reader.IsDBNull(7)
                                    ? getFormatedDatetimeFromfileName(reader.GetString(0))
                                    : reader.GetDateTime(7) // Use StartDateTime if available
                            };

                            // Step 3: Retrieve DataEntries for each file
                            string dataEntryQuery = @"
                            SELECT DE.EntryID, DE.BirdSpecies_Id, DE.Occurrences, 
                            BS.CommonName, BS.ScientificName, BS.Rarity, BS.IsRare
                            FROM DataEntries DE
                            JOIN BirdSpecies BS ON DE.BirdSpecies_Id = BS.BirdSpecies_Id
                            WHERE DE.FileID = (SELECT FileID FROM AnalyzedFiles WHERE FileName = @FileName);";

                            using (var entryCmd = new SQLiteCommand(dataEntryQuery, connection))
                            {
                                entryCmd.Parameters.AddWithValue("@FileName", dataBlock.Filename);

                                using (var entryReader = entryCmd.ExecuteReader())
                                {
                                    while (entryReader.Read())
                                    {
                                        var dataEntry = new DataBlock.DataEntry
                                        {
                                            EntryID = entryReader.GetInt32(0),
                                            Occurrences = entryReader.GetInt32(2),
                                            BirdSpecies = new BirdSpecies
                                            {
                                                BirdSpecies_Id = entryReader.GetInt32(1),
                                                CommonName = entryReader.GetString(3),
                                                ScientificName = entryReader.GetString(4),
                                                RarityLevel = entryReader.GetString(5),
                                                IsRare = entryReader.GetBoolean(6),
                                            }
                                        };

                                        dataBlock.Entries.Add(dataEntry);
                                    }
                                }
                            }

                            // Step 4: Retrieve OccurrenceDetails for each DataEntry
                            foreach (var dataEntry in dataBlock.Entries)
                            {
                                string occurrenceDetailsQuery = @"
                                SELECT TS.TimestampID, TS.StartRange, TS.EndRange, TS.Confidence
                                FROM TimeStamps TS
                                WHERE TS.EntryID = @EntryID;";

                                using (var detailCmd = new SQLiteCommand(occurrenceDetailsQuery, connection))
                                {
                                    detailCmd.Parameters.AddWithValue("@EntryID", dataEntry.EntryID);

                                    using (var detailReader = detailCmd.ExecuteReader())
                                    {
                                        while (detailReader.Read())
                                        {
                                            var occurrenceDetail = new DataBlock.DataEntry.OccurrenceDetail
                                            {
                                                TimestampID = detailReader.GetInt32(0),
                                                StartRange = detailReader.GetInt32(1),
                                                EndRange = detailReader.GetInt32(2),
                                                Confidence = detailReader.GetDouble(3)
                                            };

                                            dataEntry.OccurrenceDetails.Add(occurrenceDetail);
                                        }
                                    }
                                }
                            }

                            // Add the populated DataBlock to the batch list
                            batchDataBlocks.Add(dataBlock);
                        }
                    }
                }

                // Assign the loaded batch to a main data structure if needed
                liveDatablocks = batchDataBlocks;

                // Optional: Call methods to update UI or process data further
                // updateUIWithBatchData(batchDataBlocks);
                // graphBatchData(batchDataBlocks);

            }
            catch (Exception ex)
            {



            }
        }

        private void loadSpeciesBox()
        {
            selectSpeciesComboBox.Items.Clear();

            foreach (var block in liveDatablocks)
            {
                foreach (var entry in block.Entries)
                {
                    // Check if the species name is not already in the ComboBox
                    if (!selectSpeciesComboBox.Items.Contains(entry.BirdSpecies.CommonName))
                    {
                        selectSpeciesComboBox.Items.Add(entry.BirdSpecies.CommonName);
                    }
                }
            }
        }

        private void loadAllFilesIntoDataSet()
        {
            List<DataBlock> allDataBlocks = new List<DataBlock>();

            using (SQLiteConnection connection = new SQLiteConnection($"Data Source={BirdIDDataBase};Version=3;"))
            {
                connection.Open();
                string query = @"SELECT FileName, StartDateTime, MinConfidence, Overlap, Sensitivity, Location, Coordinates, TimeOfYearUsed, FileID FROM AnalyzedFiles";

                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var dataBlock = new DataBlock
                        {
                            Filename = reader.GetString(0),
                            StartTimeDateHHmmss = reader.IsDBNull(1) ? getFormatedDatetimeFromfileName(reader.GetString(0)) : reader.GetDateTime(1),
                            MinConfidence = reader.GetDouble(2),
                            Overlap = reader.GetDouble(3),
                            Sensitivity = reader.GetDouble(4),
                            Location = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Coordinates = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            TimeOfYearUsed = reader.GetBoolean(7)
                        };

                        int fileID = reader.GetInt32(8);

                        // Retrieve DataEntries for the file
                        string dataEntryQuery = @"SELECT DE.EntryID, DE.BirdSpecies_Id, DE.Occurrences, BS.CommonName, BS.ScientificName, BS.Rarity, BS.IsRare FROM DataEntries DE JOIN BirdSpecies BS ON DE.BirdSpecies_Id = BS.BirdSpecies_Id WHERE DE.FileID = @FileID;";
                        using (var entryCmd = new SQLiteCommand(dataEntryQuery, connection))
                        {
                            entryCmd.Parameters.AddWithValue("@FileID", fileID);
                            using (var entryReader = entryCmd.ExecuteReader())
                            {
                                while (entryReader.Read())
                                {
                                    var dataEntry = new DataBlock.DataEntry
                                    {
                                        EntryID = entryReader.GetInt32(0),
                                        Occurrences = entryReader.GetInt32(2),
                                        BirdSpecies = new BirdSpecies
                                        {
                                            BirdSpecies_Id = entryReader.GetInt32(1),
                                            CommonName = entryReader.GetString(3),
                                            ScientificName = entryReader.GetString(4),
                                            RarityLevel = entryReader.GetString(5),
                                            IsRare = entryReader.GetBoolean(6),
                                        }
                                    };

                                    // Retrieve OccurrenceDetails for each DataEntry
                                    string occurrenceDetailsQuery = @"SELECT TS.TimestampID, TS.StartRange, TS.EndRange, TS.Confidence FROM TimeStamps TS WHERE TS.EntryID = @EntryID;";
                                    using (var detailCmd = new SQLiteCommand(occurrenceDetailsQuery, connection))
                                    {
                                        detailCmd.Parameters.AddWithValue("@EntryID", dataEntry.EntryID);
                                        using (var detailReader = detailCmd.ExecuteReader())
                                        {
                                            while (detailReader.Read())
                                            {
                                                var occurrenceDetail = new DataBlock.DataEntry.OccurrenceDetail
                                                {
                                                    TimestampID = detailReader.GetInt32(0),
                                                    StartRange = detailReader.GetInt32(1),
                                                    EndRange = detailReader.GetInt32(2),
                                                    Confidence = detailReader.GetDouble(3)
                                                };
                                                dataEntry.OccurrenceDetails.Add(occurrenceDetail);
                                            }
                                        }
                                    }
                                    dataBlock.Entries.Add(dataEntry);
                                }
                            }
                        }
                        allDataBlocks.Add(dataBlock);
                    }
                }
            }
            liveDatablocks = allDataBlocks;
        }

        private List<String> getAllSpeciesUploadedToDatabase()
        {

            List<string> commonNames = new List<string>();

            string connectionString = $"Data Source={BirdIDDataBase};Version=3;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT CommonName FROM BirdSpecies";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        commonNames.Add(reader.GetString(0));
                    }
                }
            }

            return commonNames;
        }

        
        //selecting species and or graph type
        private void graphTypeSelection_SelectionChangeCommitted(object sender, EventArgs e)
        {

            displayOnGraphUpdateTable(graphTypeSelection.SelectedIndex, selectSpeciesComboBox.SelectedItem?.ToString() ?? "None");

        }

        private void selectSpeciesComboBox_SelectionChangeCommitted(object sender, EventArgs e)
        {
            selectedSpecies = selectSpeciesComboBox.SelectedItem.ToString();
            displayOnGraphUpdateTable(graphTypeSelection.SelectedIndex, selectSpeciesComboBox.SelectedItem.ToString());
        }

        //data table box display
        private void allTableOnOff_Click(object sender, EventArgs e)
        {
            if (allTableOnOff.Text == "Rare")
            {
                allTableOnOff.Text = "All";
                rareDataDetectionTable.Rows.Clear();
                updateRareDataDetectionTable();
            }
            else
            {
                allTableOnOff.Text = "Rare";
                rareDataDetectionTable.Rows.Clear();
                updateRareDataDetectionTable();
            }
        }

        private void resetTable_Click(object sender, EventArgs e)
        {
            filterColumnSelectBox.SelectedItem = null;
            filterTypeSlectBox.SelectedItem = null;
            updateRareDataDetectionTable();
        }

        private void filterColumnSelectBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (filterTypeSlectBox.SelectedItem == null || filterColumnSelectBox.SelectedItem == null)
            {
                return;
            }

            if ((filterColumnSelectBox.SelectedItem.ToString() == "Common Name" || filterColumnSelectBox.SelectedItem.ToString() == "Scientific Name"))
            {
                if (!filterTypeSlectBox.Items.Contains("Alphabetical (A-Z)"))
                {
                    filterTypeSlectBox.Items.Add("Alphabetical (A-Z)");
                    filterTypeSlectBox.Items.Add("Alphabetical (Z-A)");
                }

                if (filterTypeSlectBox.SelectedItem == null || (filterTypeSlectBox.SelectedItem.ToString() != "Alphabetical (A-Z)" && filterTypeSlectBox.SelectedItem.ToString() != "Alphabetical (Z-A)"))
                {
                    filterTypeSlectBox.SelectedItem = "Alphabetical (A-Z)";
                    rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()].SortMode = DataGridViewColumnSortMode.Automatic;

                    // Perform the sort
                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()], ListSortDirection.Ascending);
                }

                filterTypeSlectBox.Items.Remove("Largest/Highest -> Smallest/Lowest");
                filterTypeSlectBox.Items.Remove("Smallest/Lowest -> Largest/Highest");
            }
            else if ((filterColumnSelectBox.SelectedItem.ToString() != "Common Name" || filterColumnSelectBox.SelectedItem.ToString() != "Scientific Name") && filterColumnSelectBox.SelectedItem.ToString() != "Rarity Level")
            {
                if (!filterTypeSlectBox.Items.Contains("Largest/Highest -> Smallest/Lowest"))
                {
                    filterTypeSlectBox.Items.Add("Largest/Highest -> Smallest/Lowest");
                    filterTypeSlectBox.Items.Add("Smallest/Lowest -> Largest/Highest");
                }

                if (filterTypeSlectBox.SelectedItem == null || (filterTypeSlectBox.SelectedItem.ToString() != "Largest/Highest -> Smallest/Lowest" && filterTypeSlectBox.SelectedItem.ToString() != "Smallest/Lowest -> Largest/Highest"))
                {
                    filterTypeSlectBox.SelectedItem = "Largest/Highest -> Smallest/Lowest";
                    rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()].SortMode = DataGridViewColumnSortMode.Automatic;

                    // Perform the sort
                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()], ListSortDirection.Descending);
                }

                filterTypeSlectBox.Items.Remove("Alphabetical (A-Z)");
                filterTypeSlectBox.Items.Remove("Alphabetical (Z-A)");
            }
            else if (filterColumnSelectBox.SelectedItem.ToString() == "Rarity Level")
            {
                if (!filterTypeSlectBox.Items.Contains("Largest/Highest -> Smallest/Lowest"))
                {
                    filterTypeSlectBox.Items.Add("Largest/Highest -> Smallest/Lowest");
                    filterTypeSlectBox.Items.Add("Smallest/Lowest -> Largest/Highest");
                }

                if (filterTypeSlectBox.SelectedItem == null || (filterTypeSlectBox.SelectedItem.ToString() != "Largest/Highest -> Smallest/Lowest"))
                {
                    filterTypeSlectBox.SelectedItem = "Largest/Highest -> Smallest/Lowest";

                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns["raritylevel"], ListSortDirection.Ascending);

                }


                filterTypeSlectBox.Items.Remove("Alphabetical (A-Z)");
                filterTypeSlectBox.Items.Remove("Alphabetical (Z-A)");
            }
            else
            {

            }

        }

        private void filterTypeSlectBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (filterTypeSlectBox.SelectedItem == null || filterColumnSelectBox.SelectedItem == null)
            {
                return;
            }

            if (filterColumnSelectBox.SelectedItem.ToString() == "Rarity Level" && (filterTypeSlectBox.SelectedItem.ToString() == "Largest/Highest -> Smallest/Lowest" || filterTypeSlectBox.SelectedItem.ToString() == "Smallest/Lowest -> Largest/Highest"))
            {
                if (filterTypeSlectBox.SelectedItem.ToString() == "Largest/Highest -> Smallest/Lowest")
                {
                    rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()].SortMode = DataGridViewColumnSortMode.Automatic;

                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()], ListSortDirection.Ascending);
                }
                else
                {
                    rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()].SortMode = DataGridViewColumnSortMode.Automatic;

                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()], ListSortDirection.Descending);
                }
            }

            if ((filterTypeSlectBox.SelectedItem.ToString() == "Largest/Highest -> Smallest/Lowest" || filterTypeSlectBox.SelectedItem.ToString() == "Smallest/Lowest -> Largest/Highest") && filterColumnSelectBox.SelectedItem.ToString() != "Rarity Level")
            {
                if (filterTypeSlectBox.SelectedItem.ToString() == "Largest/Highest -> Smallest/Lowest")
                {
                    rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()].SortMode = DataGridViewColumnSortMode.Automatic;

                    // Perform the sort
                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()], ListSortDirection.Descending);
                }
                else
                {
                    rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()].SortMode = DataGridViewColumnSortMode.Automatic;

                    // Perform the sort
                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()], ListSortDirection.Ascending);
                }
            }

            if ((filterTypeSlectBox.SelectedItem.ToString() == "Alphabetical (A-Z)" || filterTypeSlectBox.SelectedItem.ToString() == "Alphabetical (Z-A)"))
            {
                if (filterTypeSlectBox.SelectedItem.ToString() == "Alphabetical (A-Z)")
                {
                    rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()].SortMode = DataGridViewColumnSortMode.Automatic;

                    // Perform the sort
                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()], ListSortDirection.Ascending);
                }
                else
                {
                    rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()].SortMode = DataGridViewColumnSortMode.Automatic;

                    // Perform the sort
                    rareDataDetectionTable.Sort(rareDataDetectionTable.Columns[filterColumnSelectBox.SelectedItem.ToString().Replace(" ", "").ToLower()], ListSortDirection.Descending);
                }

            }
        }

        //graph selection switcher update table
        private void displayOnGraphUpdateTable(int selectedGraphIndex, string selectedSpecies)
        {
            // Ensure that DisplayInChartSpeciesOccurence is called on the UI thread
            if (InvokeRequired)
            {
                Invoke(new Action(() =>
                {
                    graph1.Plot.Clear();

                    if (selectedGraphIndex == 0)
                    {
                        displayScatterSpeciesTime();
                    }
                    else if (selectedGraphIndex == 1)
                    {
                        displayBarSpeciesOccurence(selectedSpecies);
                    }
                    else if (selectedGraphIndex == 2)
                    {
                        averageConfidencePerBird(selectedSpecies);
                    }


                    updateRareDataDetectionTable();
                }));
            }
            else
            {
                graph1.Plot.Clear();
                if (selectedGraphIndex == 0)
                {
                    displayScatterSpeciesTime();
                }
                else if (selectedGraphIndex == 1)
                {
                    displayBarSpeciesOccurence(selectedSpecies);
                }
                else if (selectedGraphIndex == 2)
                {
                    averageConfidencePerBird(selectedSpecies);
                }



                updateRareDataDetectionTable();
            }
        }
        private void updateRareDataDetectionTable()
        {
            // Clear the table before adding new data
            rareDataDetectionTable.Rows.Clear();

            // Group bird entries by species across all datablocks
            var groupedEntries = liveDatablocks
                .SelectMany(block => block.Entries.Select(entry => new { entry, block.Filename })) // Include the Filename when flattening entries
                .GroupBy(e => new { e.entry.BirdSpecies.CommonName, e.entry.BirdSpecies.ScientificName, e.entry.BirdSpecies.RarityLevel }) // Group by bird species
                .Select(group => new
                {
                    CommonName = group.Key.CommonName,
                    ScientificName = group.Key.ScientificName,
                    RarityLevel = group.Key.RarityLevel,
                    TotalOccurrences = group.Sum(e => e.entry.Occurrences), // Sum the occurrences for all blocks
                    AverageConfidence = group.SelectMany(e => e.entry.OccurrenceDetails).DefaultIfEmpty().Average(detail => detail != null ? detail.Confidence : 0), // Calculate the average confidence across all entries
                    AllTimestamps = string.Join("; ", group.SelectMany(e => e.entry.OccurrenceDetails).Select(detail => detail.StartRange + "-" + detail.EndRange)), // Combine all timestamps
                    Filenames = string.Join("; ", group.Select(e => e.Filename).Distinct()) // Combine filenames from different blocks
                });

            // Add rows to the table based on whether the "Rare" or "All" option is selected
            foreach (var bird in groupedEntries)
            {
                if (allTableOnOff.Text == "Rare" && bird.RarityLevel != "Common")
                {
                    string[] newRow = new string[]
                    {
            bird.CommonName,
            bird.ScientificName,
            bird.RarityLevel,
            bird.TotalOccurrences.ToString(),
            bird.AverageConfidence.ToString(),
            bird.Filenames,
            bird.AllTimestamps
                    };
                    rareDataDetectionTable.Rows.Add(newRow);
                }
                else if (allTableOnOff.Text == "All")
                {
                    string[] newRow = new string[]
                    {
            bird.CommonName,
            bird.ScientificName,
            bird.RarityLevel,
            bird.TotalOccurrences.ToString(),
            bird.AverageConfidence.ToString(),
            bird.Filenames,
            bird.AllTimestamps
                    };
                    rareDataDetectionTable.Rows.Add(newRow);
                }
            }
        }

        //graph options
        private void displayBarSpeciesOccurence(string selectedSpeicies)
        {
            graph1.Plot.Clear();


            Dictionary<string, int> birdOccurence = new Dictionary<string, int>();

            // Aggregate occurrences
            foreach (DataBlock block in liveDatablocks)
            {
                foreach (var entry in block.Entries)
                {
                    if (birdOccurence.ContainsKey(entry.BirdSpecies.CommonName))
                    {
                        birdOccurence[entry.BirdSpecies.CommonName] += entry.Occurrences;
                    }
                    else
                    {
                        birdOccurence[entry.BirdSpecies.CommonName] = entry.Occurrences;
                    }
                }
            }


            // Order the dictionary by occurrences in descending order
            var orderedBirdOccurence = birdOccurence
    .OrderByDescending(pair => pair.Value) // Order by value (average confidence) in descending order
    .ToDictionary(pair => pair.Key, pair => pair.Value); // Convert to dictionary

            PlotBarBirdNameBarValueInt(orderedBirdOccurence, selectedSpecies);

        }

        private void displayScatterSpeciesTime()
        {
            graph1.Plot.Clear();

            if (selectedSpecies != "" && selectSpeciesComboBox.SelectedItem != null)
            {
                selectedSpecies = selectSpeciesComboBox.SelectedItem.ToString();
            }
            else
            {
                selectSpeciesComboBox.SelectedIndex = 0;
                selectedSpecies = selectSpeciesComboBox.SelectedItem.ToString();
            }

            List<Tuple<DateTime, double>> dateTimeStampConfidence = new List<Tuple<DateTime, double>>();
            DateTime StartTimeDate = DateTime.UtcNow;

            foreach (DataBlock block in liveDatablocks)
            {
                StartTimeDate = block.StartTimeDateHHmmss;
                foreach (var entry in block.Entries)
                {
                    if (entry.BirdSpecies.CommonName == selectedSpecies)
                    {
                        foreach (var timeStampAndConfidence in entry.OccurrenceDetails)
                        {
                            DateTime currentSampleDateTime = DateTime.UtcNow;

                            string[] parts = new string[] { timeStampAndConfidence.StartRange.ToString(), timeStampAndConfidence.EndRange.ToString() };
                            int CurrentMiddleSeconds = 0;

                            if (parts.Length == 2 && int.TryParse(parts[0], out int startSeconds) && int.TryParse(parts[1], out int endSeconds))
                            {
                                // Calculate the middle of the range
                                CurrentMiddleSeconds = (startSeconds + endSeconds) / 2;
                            }
                            currentSampleDateTime = StartTimeDate.AddSeconds(CurrentMiddleSeconds);

                            dateTimeStampConfidence.Add(new Tuple<DateTime, double>(currentSampleDateTime, timeStampAndConfidence.Confidence));
                        }
                    }
                }
            }

            //order dattime
            dateTimeStampConfidence = dateTimeStampConfidence.OrderBy(t => t.Item1).ToList();


            //ai!!
            // Define the threshold for a significant gap (e.g., 1 hour)
            TimeSpan significantGap = TimeSpan.FromHours(1);

            // Create new lists to hold the adjusted data
            var adjustedXValues = new List<double>();
            var adjustedYValues = new List<double>();

            // Iterate through the sorted data
            for (int i = 0; i < dateTimeStampConfidence.Count; i++)
            {
                // Add the current point
                adjustedXValues.Add(dateTimeStampConfidence[i].Item1.ToOADate());
                adjustedYValues.Add(dateTimeStampConfidence[i].Item2);

                // Check for significant gaps (if not the last point)
                if (i < dateTimeStampConfidence.Count - 1)
                {
                    DateTime current = dateTimeStampConfidence[i].Item1;
                    DateTime next = dateTimeStampConfidence[i + 1].Item1;

                    if (next - current > significantGap)
                    {
                        // Insert a small gap in X-axis to simulate the squiggle
                        adjustedXValues.Add(current.AddTicks(1).ToOADate());
                        adjustedYValues.Add(double.NaN); // Add NaN to create a gap
                    }
                }
            }

            //me
            graph1.Plot.Add.Scatter(adjustedXValues.ToArray(), adjustedYValues.ToArray());
            graph1.Plot.Axes.DateTimeTicksBottom();
            graph1.Plot.Title($"{selectedSpecies} Detection Confidence Over Time");
            graph1.Plot.XLabel("Timestamp");
            graph1.Plot.YLabel("Confidence");
            graph1.Refresh();
        }

        private void averageConfidencePerBird(string selectedSpecies)
        {

            Dictionary<string, (double TotalConfidence, int Count)> birdConfidenceData = new Dictionary<string, (double, int)>();

            foreach (var dataBlock in liveDatablocks)
            {
                foreach (var dataEntry in dataBlock.Entries)
                {
                    // Calculate the average confidence for the current dataEntry's OccurrenceDetails
                    if (dataEntry.OccurrenceDetails.Count > 0)
                    {
                        double averageConfidence = dataEntry.OccurrenceDetails.Average(od => od.Confidence);

                        // If the bird is already in the dictionary, accumulate the confidence and increment the count
                        if (birdConfidenceData.ContainsKey(dataEntry.BirdSpecies.CommonName))
                        {
                            var currentData = birdConfidenceData[dataEntry.BirdSpecies.CommonName];
                            birdConfidenceData[dataEntry.BirdSpecies.CommonName] = (currentData.TotalConfidence + averageConfidence, currentData.Count + 1);
                        }
                        else
                        {
                            // Otherwise, add the bird to the dictionary with the current confidence and count of 1
                            birdConfidenceData[dataEntry.BirdSpecies.CommonName] = (averageConfidence, 1);
                        }
                    }
                }
            }

            // Now calculate the average confidence for each bird and store it in a new dictionary
            Dictionary<string, double> birdAverageConfidence = new Dictionary<string, double>();

            foreach (var entry in birdConfidenceData)
            {
                birdAverageConfidence[entry.Key] = entry.Value.TotalConfidence / entry.Value.Count;
            }

            // Order the dictionary alphabetically by bird name (key)
            var orderedBirdsAlphabetically = birdAverageConfidence
                .OrderBy(pair => pair.Key) // Order by key (bird name) alphabetically
                .ToDictionary(pair => pair.Key, pair => pair.Value); // Convert to dictionary


            PlotBarBirdNameBarValueDouble(orderedBirdsAlphabetically, selectedSpecies);



        }


        //prep data for plotting
        private void PlotBarBirdNameBarValueDouble(Dictionary<string, double> birdNameBarValue, string selectedSpecies)
        {
            // Prepare data for the bar plot
            string[] birdNames = birdNameBarValue.Select(pair => pair.Key).ToArray();
            double[] value = birdNameBarValue.Select(pair => (double)pair.Value).ToArray(); // Convert to double array

            List<ScottPlot.Bar> bars = new List<Bar> { };

            for (int i = 0; i < birdNames.Count(); i++)
            {
                ScottPlot.Color barColour = Colors.LightBlue;

                if (birdNames[i] == selectedSpecies)
                {
                    barColour = Colors.Red;
                }
                else
                {
                    barColour = Colors.LightBlue;
                }

                ScottPlot.Bar bar = new()
                {
                    Position = i,
                    Value = value[i],
                    FillColor = barColour,
                    LineColor = barColour.Darken(0.5),
                };

                bars.Add(bar);

            }




            foreach (var bar in bars)
            {
                graph1.Plot.Add.Bar(bar);
            }

            graph1.Plot.Axes.Margins(bottom: 0);




            // Create a tick for each bar using bird names
            Tick[] ticks = new Tick[birdNames.Length];
            for (int i = 0; i < birdNames.Length; i++)
            {
                ticks[i] = new Tick(i, birdNames[i]); // Use xPositions[i] for the x-position
            }

            graph1.Plot.Axes.AutoScale();
            graph1.Plot.Axes.NumericTicksBottom();
            // Remove the DateTime tick generator




            graph1.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
            graph1.Plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
            graph1.Plot.Axes.Bottom.TickLabelStyle.Alignment = ScottPlot.Alignment.MiddleLeft;


            // determine the width of the largest tick label
            float largestLabelWidth = 0;
            SKPaint paint = new SKPaint();
            paint.Color = SKColors.Black;
            // Iterate over each tick
            foreach (Tick tick in ticks)
            {
                // Measure the size of the tick label with the paint settings
                PixelSize size = graph1.Plot.Axes.Bottom.TickLabelStyle.Measure(tick.Label, paint).Size;

                // Update the largest label width
                largestLabelWidth = Math.Max(largestLabelWidth, size.Width);
            }

            // ensure axis panels do not get smaller than the largest label
            graph1.Plot.Axes.Bottom.MinimumSize = largestLabelWidth;
            graph1.Plot.Axes.Right.MinimumSize = largestLabelWidth;


            // Customize the plot
            graph1.Plot.Title("Average Certainty Per Bird");
            graph1.Plot.XLabel("Birds");
            graph1.Plot.YLabel("Average Certainty");

            // Refresh the plot to display
            graph1.Refresh();
            graph1.Plot.Axes.AutoScale();
        }

        private void PlotBarBirdNameBarValueInt(Dictionary<string, int> birdNameBarValue, string selectedSpecies)
        {
            // Prepare data for the bar plot
            string[] birdNames = birdNameBarValue.Select(pair => pair.Key).ToArray();
            double[] value = birdNameBarValue.Select(pair => (double)pair.Value).ToArray(); // Convert to double array

            List<ScottPlot.Bar> bars = new List<Bar> { };

            for (int i = 0; i < birdNames.Count(); i++)
            {
                ScottPlot.Color barColour = Colors.LightBlue;

                if (birdNames[i] == selectedSpecies)
                {
                    barColour = Colors.Red;
                }
                else
                {
                    barColour = Colors.LightBlue;
                }

                ScottPlot.Bar bar = new()
                {
                    Position = i,
                    Value = value[i],
                    FillColor = barColour,
                    LineColor = barColour.Darken(0.5),
                };

                bars.Add(bar);

            }




            foreach (var bar in bars)
            {
                graph1.Plot.Add.Bar(bar);
            }

            graph1.Plot.Axes.Margins(bottom: 0);




            // Create a tick for each bar using bird names
            Tick[] ticks = new Tick[birdNames.Length];
            for (int i = 0; i < birdNames.Length; i++)
            {
                ticks[i] = new Tick(i, birdNames[i]); // Use xPositions[i] for the x-position
            }

            graph1.Plot.Axes.AutoScale();
            graph1.Plot.Axes.NumericTicksBottom();
            // Remove the DateTime tick generator




            graph1.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
            graph1.Plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
            graph1.Plot.Axes.Bottom.TickLabelStyle.Alignment = ScottPlot.Alignment.MiddleLeft;


            // determine the width of the largest tick label
            float largestLabelWidth = 0;
            SKPaint paint = new SKPaint();
            paint.Color = SKColors.Black;
            // Iterate over each tick
            foreach (Tick tick in ticks)
            {
                // Measure the size of the tick label with the paint settings
                PixelSize size = graph1.Plot.Axes.Bottom.TickLabelStyle.Measure(tick.Label, paint).Size;

                // Update the largest label width
                largestLabelWidth = Math.Max(largestLabelWidth, size.Width);
            }

            // ensure axis panels do not get smaller than the largest label
            graph1.Plot.Axes.Bottom.MinimumSize = largestLabelWidth;
            graph1.Plot.Axes.Right.MinimumSize = largestLabelWidth;


            // Customize the plot
            graph1.Plot.Title("Bird Occurrences");
            graph1.Plot.XLabel("Birds");
            graph1.Plot.YLabel("Occurrences");

            // Refresh the plot to display
            graph1.Refresh();
            graph1.Plot.Axes.AutoScale();
        }






        //species metrics
        private void speciesMetricsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.MaximizeBox = true;
            fileProccessingPanel.Visible = false;
            settingsPanel.Visible = false;
            ExportPanel.Visible = false;
            analysisPanel.Visible = false;

            this.MinimumSize = new System.Drawing.Size(440, 740);
            this.MaximumSize = new System.Drawing.Size(0, 0);
            this.Size = new System.Drawing.Size(440, 740);



            // original 590, 760
            // Calculate new position for the form to center it
            int x = (Screen.PrimaryScreen.WorkingArea.Width - this.Width) / 2;
            int y = (Screen.PrimaryScreen.WorkingArea.Height - this.Height) / 2;

            this.SetDesktopLocation(x, y); // Set the new location

            speciesMetricsPanel.Visible = true;

            List<string> allSpeciesUsed = getAllSpeciesUploadedToDatabase();

            comboBox1.Items.Clear();
            comboBox5.Items.Clear();
            comboBox6.Items.Clear();

            foreach (string s in allSpeciesUsed) { comboBox1.Items.Add(s); }

            if (speciesMetricSelectedSpeices != null)
            {
                comboBox1.SelectedItem = speciesMetricSelectedSpeices;
            }

        }

        private void loadExportLocation()
        {
            exportLocationLabel.Text = $"Export to: {outputFilePath}";
        }

        private void listBatchesExport()
        {
            string query = "SELECT BatchName FROM Batches";
            string connectionString = $"Data Source={BirdIDDataBase}; Version=3;";

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            availableFilesBox.Items.Clear(); // Clear existing items before adding new ones

                            while (reader.Read())
                            {
                                string batchName = reader["BatchName"].ToString();
                                availableFilesBox.Items.Add(batchName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading batches: {ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text.ToString() == "By Block")
            {
                button1.Text = "By File";
                panel18.Enabled = false;
                panel19.Enabled = true;
            }
            else
            {
                button1.Text = "By Block";
                ; panel18.Enabled = true;
                panel19.Enabled = false;
            }
        }

        private void comboBox1_SelectionChangeCommitted(object sender, EventArgs e)
        {
            speciesMetricSelectedSpeices = comboBox1.SelectedItem.ToString();
            populateSelectBlockAndFileMetricsPage();

            if (comboBox5.SelectedItem == null)
            {
                comboBox5.SelectedIndex = 0;
            }

            if (comboBox6.SelectedItem == null) { comboBox6.SelectedIndex = 0; }

            liveDatablocks.Clear();
            List<string> itemsList = comboBox6.Items.Cast<string>().ToList();
            if (itemsList[comboBox6.SelectedIndex] != "All" && itemsList[comboBox6.SelectedIndex] != "")
            {
                loadSingleFileIntoDatablocks(comboBox6.SelectedItem.ToString());
                liveDatablocks.Add(mainDataBlock);
            }
            else
            {
                loadAllFilesIntoDataSet();
            }

            speciesMetricsCommit();
        }

        private void populateSelectBlockAndFileMetricsPage()
        {
            liveDatablocks.Clear();


            List<string> blocks = new List<string>();
            List<string> files = new List<string>();

            files.Add("All");
            blocks.Add("All");

            string connectionString = $"Data Source={BirdIDDataBase};Version=3;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT FileName, StartDateTime, MinConfidence, Overlap, Sensitivity, Location, Coordinates, TimeOfYearUsed FROM AnalyzedFiles";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string fileName = reader.GetString(0);
                        string minConfidence = reader.GetDouble(2).ToString();
                        string overlap = reader.GetDouble(3).ToString();
                        string sensitivity = reader.GetDouble(4).ToString();
                        string location = reader.GetString(5);
                        string coordinates = reader.GetString(6);
                        string timeOfYearUsed = reader.GetBoolean(7).ToString();

                        string locationOrCoordinates = !string.IsNullOrEmpty(location) ? location : coordinates;
                        string formattedString = $"{fileName} - MinConf: {minConfidence}, Location/Coordinates: {locationOrCoordinates}, Overlap: {overlap}, Sensitivity: {sensitivity}, TimeOfYearUsed: {timeOfYearUsed}";

                        files.Add(formattedString);
                    }
                }

                query = "SELECT BatchName FROM Batches";
                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        blocks.Add(reader.GetString(0));
                    }
                }
            }

            foreach (var block in blocks)
            {
                if (!comboBox5.Items.Contains(block))
                {
                    comboBox5.Items.Add(block);
                }
            }

            foreach (var file in files)
            {
                if (!comboBox6.Items.Contains(file))
                {
                    comboBox6.Items.Add(file);
                }
            }
        }

        private void comboBox6_SelectionChangeCommitted(object sender, EventArgs e)
        {
            liveDatablocks.Clear();

            List<string> itemsList = comboBox6.Items.Cast<string>().ToList();


            if (itemsList[comboBox6.SelectedIndex] != "All" && itemsList[comboBox6.SelectedIndex] != "")
            {
                loadSingleFileIntoDatablocks(comboBox6.SelectedItem.ToString());
                liveDatablocks.Add(mainDataBlock);
            }
            else
            {
                loadAllFilesIntoDataSet();
            }

            speciesMetricsCommit();
        }

        private void comboBox5_SelectionChangeCommitted(object sender, EventArgs e)
        {
            liveDatablocks.Clear();

            List<string> itemsList = comboBox5.Items.Cast<string>().ToList();


            if (itemsList[comboBox5.SelectedIndex] != "All" && itemsList[comboBox5.SelectedIndex] != "")
            {
                loadBatchIntoDatablocks(comboBox5.SelectedItem.ToString() + " (ID: 1)");
            }
            else
            {
                loadAllFilesIntoDataSet();
            }

            speciesMetricsCommit();
        }

        private void speciesMetricsCommit()
        {
            string SMsciName = null;
            string SMcomName = speciesMetricSelectedSpeices;

            int SMtotalOccurences = 0;
            double SMaverageCertainty = 0;

            string SMmostOccurencesIn = string.Empty;
            int SMmostOccurencesCount = 0;

            bool SMappearsInDataSet = false;
            double SMtotalCirtainty = 0;
            double cirtaintyCounter = 0;



            foreach (var block in liveDatablocks)
            {
                int occurencesInThisBlock = 0;

                foreach (var entry in block.Entries)
                {
                    if (entry.BirdSpecies.CommonName == speciesMetricSelectedSpeices)
                    {
                        SMappearsInDataSet = true;

                        if (SMsciName == null)
                        {
                            SMsciName = entry.BirdSpecies.ScientificName;
                        }

                        occurencesInThisBlock = entry.Occurrences;
                        SMtotalOccurences += entry.Occurrences;

                        foreach (var occurence in entry.OccurrenceDetails)
                        {
                            SMtotalCirtainty += occurence.Confidence;
                            cirtaintyCounter++;
                        }
                    }
                }

                if (SMmostOccurencesCount < occurencesInThisBlock)
                {
                    SMmostOccurencesCount = occurencesInThisBlock;
                    SMmostOccurencesIn = block.Filename;
                }

            }

            SMaverageCertainty = SMtotalCirtainty / cirtaintyCounter;

            ScientificNameLabel.Text = "Scientific Name: " + SMsciName;
            commonNameLabel.Text = "Common Name: " + SMcomName;
            totalOccurences.Text = "Total Occurences: " + SMtotalOccurences;
            averageCertaintyLabel.Text = "Average Certainty: " + SMaverageCertainty;
            mostOccurencesIn.Text = "Most Occurences In: " + SMmostOccurencesIn;
            makesAnOccurence.Text = "Appears In This Dataset: " + SMappearsInDataSet;
        }

        

        //export data 
        private void exportDataToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(440, 740);
            this.Size = new System.Drawing.Size(440, 740);
            this.MaximumSize = new System.Drawing.Size(0, 0);
            ExportPanel.Visible = true;
            fileProccessingPanel.Visible = false;
            settingsPanel.Visible = false;
            analysisPanel.Visible = false;
            speciesMetricsPanel.Visible = false;



            listBatchesExport();

            loadExportLocation();
        }

        private void Export_Click(object sender, EventArgs e)
        {
            //export batch to excell

            string outputFilePathLocal = "";

            if (availableFilesBox.SelectedItems.Count <= 0) { return; }
            loadBatchIntoDatablocks(availableFilesBox.SelectedItem.ToString() + " (ID: 1)");

            //export livedatablocks

            if (outputFilePath == "Desktop")
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd__HH-mm-ss");
                outputFilePathLocal = System.IO.Path.Combine(desktopPath, $"AnalysisResults_{timestamp}.xlsx");
            }
            else
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd__HH-mm-ss");
                outputFilePathLocal = System.IO.Path.Combine(outputFilePath, $"AnalysisResults_{timestamp}.xlsx");
            }

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Results");

                int currentRow = 1;

                foreach (var batch in liveDatablocks)
                {

                    string fileName = batch.Filename;
                    // Write the file name as header
                    worksheet.Cell(currentRow, 1).Value = fileName;
                    currentRow++;
                    // Write the header row
                    worksheet.Cell(currentRow, 1).Value = "Common Name";
                    worksheet.Cell(currentRow, 2).Value = "Scientific Name";
                    worksheet.Cell(currentRow, 3).Value = "Occurrences";
                    worksheet.Cell(currentRow, 4).Value = "Timestamps and Confidence";
                    currentRow++;

                    // Write the results
                    foreach (var result in batch.Entries)
                    {

                        worksheet.Cell(currentRow, 1).Value = result.BirdSpecies.CommonName;
                        worksheet.Cell(currentRow, 2).Value = result.BirdSpecies.ScientificName;
                        worksheet.Cell(currentRow, 3).Value = result.Occurrences;


                        int ColumnCount = 4;
                        foreach (var entry in result.OccurrenceDetails)
                        {
                            worksheet.Cell(currentRow, ColumnCount).Value = entry.StartRange + "-" + entry.EndRange + ", " + entry.Confidence;
                            ColumnCount++;
                        }

                        currentRow++;
                    }

                    // Add a few lines of separation between file results
                    currentRow += 3;
                }

                workbook.SaveAs(outputFilePathLocal);
            }


        }





        //settings page
        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(440, 740);
            this.MaximumSize = new System.Drawing.Size(0, 0);
            this.Size = new System.Drawing.Size(440, 740);
            settingsPanel.Visible = true;
            fileProccessingPanel.Visible = false;
            analysisPanel.Visible = false;
            ExportPanel.Visible = false;
            speciesMetricsPanel.Visible = false;


            listBatches();
        }

        private void listBatches()
        {
            string query = "SELECT BatchName FROM Batches";
            string connectionString = $"Data Source={BirdIDDataBase}; Version=3;";

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SQLiteCommand(query, connection))
                    {
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            listOfBatches.Items.Clear(); // Clear existing items before adding new ones

                            while (reader.Read())
                            {
                                string batchName = reader["BatchName"].ToString();
                                listOfBatches.Items.Add(batchName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading batches: {ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void saveDefaultSettings_Click(object sender, EventArgs e)
        {
            try
            {
                // Retrieve values from controls
                double newConfidenceValue;
                if (!double.TryParse(defaultMinConfSettingBox.Text, out newConfidenceValue))
                {
                    MessageBox.Show("Invalid confidence value. Please enter a valid number.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string newOutputFilePath = defaultOutputFolderLocationLabel.Text;
                string newInputFilePath = inputLocationDefaultlabel.Text;
                string newDefaultLocation = defaultLocationSettingBox.SelectedItem?.ToString();

                // Open the file for writing
                using (StreamWriter writer = new StreamWriter(settingsFilePath))
                {
                    // Write the updated settings to the file
                    writer.WriteLine(newConfidenceValue.ToString("G")); // Write confidence value
                    writer.WriteLine(newDefaultLocation ?? ""); // Write default location
                    writer.WriteLine(newOutputFilePath); // Write output file path
                    writer.WriteLine(newInputFilePath); // Write input file path
                    writer.WriteLine(birdnetPackageVersion);

                    // Write locations
                    writer.WriteLine(); // Add a blank line before the "Locations" header
                    writer.WriteLine("Locations"); // Header for locations

                    foreach (var location in savedLocations)
                    {
                        writer.WriteLine($"{location.Name} {location.Latitude},{location.Longitude}");
                    }
                }

                MessageBox.Show("Settings saved successfully.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception occurred while saving settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }


        }

        private void defaultOutputFolderLocationLabel_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                folderBrowserDialog.Description = "Select a folder"; // Optional: Description for the dialog
                folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer; // Optional: Default root folder

                DialogResult result = folderBrowserDialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    // Get the selected folder path
                    string selectedFolderPath = folderBrowserDialog.SelectedPath;

                    // Use the selected folder path
                    outputFilePath = selectedFolderPath;
                    defaultOutputFolderLocationLabel.Text = selectedFolderPath;

                    // Optionally update some UI elements or settings with the selected path
                    // e.g., folderPathTextBox.Text = selectedFolderPath;
                }
            }
        }

        private void inputLocationDefaultlabel_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                folderBrowserDialog.Description = "Select a folder"; // Optional: Description for the dialog
                folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer; // Optional: Default root folder

                DialogResult result = folderBrowserDialog.ShowDialog();

                if (result == DialogResult.OK)
                {
                    // Get the selected folder path
                    string selectedFolderPath = folderBrowserDialog.SelectedPath;

                    // Use the selected folder path
                    inputFilePath = selectedFolderPath;
                    inputLocationDefaultlabel.Text = selectedFolderPath;

                    // Optionally update some UI elements or settings with the selected path
                    // e.g., folderPathTextBox.Text = selectedFolderPath;
                }
            }
        }

        private void addNewLocationButton_Click(object sender, EventArgs e)
        {
            try
            {
                savedLocations.Add(new Location(newLocationNameSettingInput.Text, double.Parse(newLatitudeBox.Text), double.Parse(newLongitudeBox.Text)));
            }
            catch
            {
                MessageBox.Show($"Failed to add location.\nPlease check your latitude longitude values!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateSavedLocations();

            newLongitudeBox.Clear();
            newLatitudeBox.Clear();
            newLocationNameSettingInput.Clear();

            if (newLongitudeBox.Text != "Enter Longitude" && newLongitudeBox.Text == "")
            {
                newLongitudeBox.Text = "Enter Longitude";
            }
            if (newLatitudeBox.Text != "Enter Latitude" && newLatitudeBox.Text == "")
            {
                newLatitudeBox.Text = "Enter Latitude";
            }
            if (newLocationNameSettingInput.Text != "Enter Location Name" && newLocationNameSettingInput.Text == "")
            {
                newLocationNameSettingInput.Text = "Enter Location Name";
            }

            foreach (var location in savedLocations)
            {
                predefinedCordsBox.Items.Add(location.Name);
            }


        }

        private void fileProccessingPanel_Click(object sender, EventArgs e)
        {
            fileProccessingPanel.Focus();
        }

        private void refreshAnalysedFilesButton_Click(object sender, EventArgs e)
        {
            refreshAnalysedFiles();
        }

        private void defaultMinConfSettingBox_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (double.Parse(defaultMinConfSettingBox.Text) > 1 || double.Parse(defaultMinConfSettingBox.Text) < 0)
                {
                    defaultMinConfSettingBox.Text = confidenceValue.ToString();
                }
            }
            catch
            {
                defaultMinConfSettingBox.Text = confidenceValue.ToString();
            }

        }

        //enter leave updates
        private void newLatitudeBox_Enter(object sender, EventArgs e)
        {
            if (newLatitudeBox.Text == "Enter Latitude")
            {
                newLatitudeBox.Text = "";
            }
        }

        private void newLatitudeBox_Leave(object sender, EventArgs e)
        {
            if (newLatitudeBox.Text != "Enter Latitude" && newLatitudeBox.Text == "")
            {
                newLatitudeBox.Text = "Enter Latitude";
            }
        }

        private void newLongitudeBox_Enter(object sender, EventArgs e)
        {
            if (newLongitudeBox.Text == "Enter Longitude")
            {
                newLongitudeBox.Text = "";
            }
        }

        private void newLongitudeBox_Leave(object sender, EventArgs e)
        {
            if (newLongitudeBox.Text != "Enter Longitude" && newLongitudeBox.Text == "")
            {
                newLongitudeBox.Text = "Enter Longitude";
            }
        }

        private void newLocationNameSettingInput_Enter(object sender, EventArgs e)
        {
            if (newLocationNameSettingInput.Text == "Enter Location Name")
            {
                newLocationNameSettingInput.Text = "";
            }
        }

        private void newLocationNameSettingInput_Leave(object sender, EventArgs e)
        {
            if (newLocationNameSettingInput.Text != "Enter Location Name" && newLocationNameSettingInput.Text == "")
            {
                newLocationNameSettingInput.Text = "Enter Location Name";
            }
        }

        private void clearDatabaseButton_Click(object sender, EventArgs e)
        {
            string connectionString = $"Data Source={BirdIDDataBase};Version=3;";



            string query = @"
                PRAGMA foreign_keys = OFF;

                DELETE FROM TimeStamps;
                DELETE FROM DataEntries;
                DELETE FROM AnalyzedFiles;
                DELETE FROM BatchFiles;
                DELETE FROM Batches;
                DELETE FROM BirdSpecies;
                DELETE FROM sqlite_sequence;

                PRAGMA foreign_keys = ON;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                try
                {
                    connection.Open();  // Open the connection

                    using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.ExecuteNonQuery();  // Execute the query
                    }

                    MessageBox.Show("All tables cleared successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    connection.Close();  // Close the connection
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (listOfBatches.SelectedItems.Count > 0)
            {
                RemoveBatch(listOfBatches.SelectedItem.ToString());
            }
        }
        private void RemoveBatch(string batchName)
        {
            string connectionString = $"Data Source={BirdIDDataBase}; Version=3;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Get the BatchID of the batch to be deleted
                        int batchId = -1;
                        string getBatchIdQuery = "SELECT BatchID FROM Batches WHERE BatchName = @BatchName";
                        using (var cmd = new SQLiteCommand(getBatchIdQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@BatchName", batchName);
                            var result = cmd.ExecuteScalar();
                            if (result != null)
                            {
                                batchId = Convert.ToInt32(result);
                            }
                        }

                        if (batchId == -1)
                        {
                            MessageBox.Show("Batch not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // Get files associated with this batch
                        string getFilesQuery = "SELECT FileID FROM BatchFiles WHERE BatchID = @BatchID";
                        var fileIds = new List<int>();

                        using (var cmd = new SQLiteCommand(getFilesQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@BatchID", batchId);
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    fileIds.Add(reader.GetInt32(0));
                                }
                            }
                        }

                        // Remove links in BatchFiles table
                        string deleteBatchFilesQuery = "DELETE FROM BatchFiles WHERE BatchID = @BatchID";
                        using (var cmd = new SQLiteCommand(deleteBatchFilesQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@BatchID", batchId);
                            cmd.ExecuteNonQuery();
                        }

                        // Remove Batch itself
                        string deleteBatchQuery = "DELETE FROM Batches WHERE BatchID = @BatchID";
                        using (var cmd = new SQLiteCommand(deleteBatchQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@BatchID", batchId);
                            cmd.ExecuteNonQuery();
                        }

                        // Remove files that are no longer linked to any batch
                        foreach (int fileId in fileIds)
                        {
                            string checkFileQuery = "SELECT COUNT(*) FROM BatchFiles WHERE FileID = @FileID";
                            using (var cmd = new SQLiteCommand(checkFileQuery, connection))
                            {
                                cmd.Parameters.AddWithValue("@FileID", fileId);
                                int count = Convert.ToInt32(cmd.ExecuteScalar());

                                if (count == 0) // Only delete if it's not linked to another batch
                                {
                                    string deleteFileQuery = "DELETE FROM AnalyzedFiles WHERE FileID = @FileID";
                                    using (var deleteCmd = new SQLiteCommand(deleteFileQuery, connection))
                                    {
                                        deleteCmd.Parameters.AddWithValue("@FileID", fileId);
                                        deleteCmd.ExecuteNonQuery();
                                    }
                                }
                            }
                        }

                        transaction.Commit();

                        listBatches();
                        MessageBox.Show("Batch removed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        MessageBox.Show($"Error removing batch: {ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }





        //focusing
        private void panel1_Click(object sender, EventArgs e)
        {
            this.Focus();
        }






        //other
        /// <summary>
        /// Event handler for the process timer tick event, updates the elapsed time label.
        /// </summary>
        private void ProccessTimer_Tick(object sender, EventArgs e)
        {
            UpdateElapsedTimeLabel();
        }
    }
}

