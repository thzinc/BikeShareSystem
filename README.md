# Bike Share System

Powering the interactive #BikeShareChallenges for @BikeDTLABot and others

This is a program that uses the Twitter streaming API to receive Tweets and then reply to them with challenges.

[![@BikeDTLABot demo](https://media.giphy.com/media/l1J9KoS2JvBdDT9oQ/giphy.gif)](https://www.youtube.com/watch?v=XOn-Cjjpgq8)

## Quickstart

Create a `settings.json` file with the configuration of the [GBFS](https://github.com/NABSA/gbfs) feed information and Twitter account information for the bots this will power.

### Example `settings.json`

```json
{
    "bike_share_systems": [
        {
            "system_id": "bcycle_lametro",
            "manifest_url": "https://gbfs.bcycle.com/bcycle_lametro/gbfs.json",
            "station_name_replacements": {
                "Santa Monica Expo Line": "SMC Expo Line",
                "Station": "Sta.",
                "North": "N.",
                "East": "E.",
                "South": "S.",
                "West": "W.",
                "Place": "Pla",
                "Center": "Ctr",
                "Pop-up Station": "Pop-up"
            }
        }
    ],
    "bots": [
        {
            "area_of_interest": {
                "center": {
                    "latitude": 34.0432071,
                    "longitude": -118.2849729
                },
                "radius": 10
            },
            "bike_share_system_ids": [
                "bcycle_lametro"
            ],
            "twitter": {
                "screen_name": "YourTwitterHandle",
                "consumer_key": "Consumer key",
                "consumer_secret": "Consumer secret",
                "access_token_key": "Access token key",
                "access_token_secret": "Access token secret"
            },
            "replies": {
                "challenge": {
                    "location_not_found": "I can't find {location}, so I don't know how to challenge you. Try tagging your reply with your location.",
                    "messages": [
                        "Go from {start} to {end} ({distance}, {duration}) {link}",
                        "Try {start} to {end} in {duration} ({distance}) {link}",
                        "How about {start} to {end}? ({distance}, {duration}) {link}",
                        "{start} to {end} in {duration}. You got this! ({distance}) {link}"
                    ]
                }
            }
        }
    ],
    "google_api_key": "Google API key with Places API enabled"
}
```

If running this program locally:

```bash
# assumes settings.json is located at /tmp/data
dotnet restore # Only necessary to run the first time
dotnet run -- /tmp/data/settings.json
```

If running the Docker image:

```bash
# assumes settings.json is located at /tmp/data
docker run -d -it -v /tmp/data:/data thzinc/bikesharesystem
```

## Building

[![Docker Build Status](https://img.shields.io/docker/build/thzinc/bikesharesystem.svg)](https://hub.docker.com/r/thzinc/bikesharesystem/)

The [Dockerfile](Dockerfile) is able to build the program and produce the Docker image. If working and running locally, you'll need .NET Core 2.0 installed.

### Building the Docker image

The following builds a local Docker image named and tagged `thzinc/bikesharesystem:devel`

```bash
docker build -t thzinc/bikesharesystem:devel .
```

### Building the program locally

```bash
dotnet restore
dotnet build
```

## Code of Conduct

We are committed to fostering an open and welcoming environment. Please read our [code of conduct](CODE_OF_CONDUCT.md) before participating in or contributing to this project.

## Contributing

We welcome contributions and collaboration on this project. Please read our [contributor's guide](CONTRIBUTING.md) to understand how best to work with us.

## License and Authors

[![Daniel James logo](https://secure.gravatar.com/avatar/eaeac922b9f3cc9fd18cb9629b9e79f6.png?size=16) Daniel James](https://github.com/thzinc)

[![license](https://img.shields.io/github/license/thzinc/BikeShareSystem.svg)](https://github.com/thzinc/BikeShareSystem/blob/master/LICENSE)
[![GitHub contributors](https://img.shields.io/github/contributors/thzinc/BikeShareSystem.svg)](https://github.com/thzinc/BikeShareSystem/graphs/contributors)

This software is made available by Daniel James under the MIT license.