import importlib.metadata
from birdnetlib import Recording
from birdnetlib.analyzer import Analyzer
from datetime import datetime
import os
import sys
import librosa
import json
import argparse


def main():
    parser = argparse.ArgumentParser(description='Process some audio recordings.')
    parser.add_argument('file', type=str, help='Path to the audio file')
    parser.add_argument('lat', type=float, help='Latitude of the recording')
    parser.add_argument('lon', type=float, help='Longitude of the recording')
    parser.add_argument('date', type=str, nargs='?', default=None, help='Date of the recording in YYYY-MM-DD format (optional)')
    parser.add_argument('min_conf', type=float, help='Minimum confidence level for detection')
    parser.add_argument('overlap', type=float, help='overlap')
    parser.add_argument('sensitivity', type=float, help='sensitivity')
   


    args = parser.parse_args()

    date = None
    if args.date:
        date = datetime.strptime(args.date, '%Y-%m-%d')

    # Load and initialize the BirdNET-Analyzer models.
    analyzer = Analyzer(
    )

    recording = Recording(
        analyzer,
        args.file,
        sensitivity=args.sensitivity,
        lat=args.lat,
        lon=args.lon,
        date=date,
        min_conf=args.min_conf,
        overlap=args.sensitivity
    )
    recording.analyze()

    # Output results as JSON
    output = {
        "birdnetlib_version": importlib.metadata.version("birdnetlib"),  # Add a key for the version
        "detections": recording.detections  # Directly use the dictionary
    }
    print(json.dumps(output, indent=4))

if __name__ == '__main__':
    main()