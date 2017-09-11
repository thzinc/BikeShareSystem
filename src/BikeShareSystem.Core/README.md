# Actor architecture

This is an actor system with a number of different actors passing messages back and forth.

## Parent?

Root actor

Starts all Bot and BikeShareSystem actors

## Bot

Representation of a bot that interacts with people through Twitter. One instance per Twitter bot.

Has configuration

* Twitter handle & API info
* Geographic area of interest
    * Point and radius should be enough
* Schedules
    * Hashtags to use in posts
    * Examples
        * 06:30: Morning {hashtag} #BikeShareChallenge: go from {start} to {end} ({distance}, {duration}) {link}
        * 11:30: Mid-day {hashtag} #BikeShareChallenge: go from {start} to {end} ({distance}, {duration}) {link}
        * 16:30: Evening {hashtag} #BikeShareChallenge: go from {start} to {end} ({distance}, {duration}) {link}
* Selected BikeShareSystems

* Scheduled posts
    * Morning, mid-day, and evening #BikeShareChallenge
* Replies
    * Challenge me
        * Use tweet location if available
        * Avoid repeat challenges in reply thread
        * Reply example
            * {user}, go from {start} to {end} ({distance}, {duration}) {link}
        * Other challenge request examples
            * Challenge me from 523 W 6th
            * Challenge me to Union Station
            * Challenge me from 6th and hope to Chinatown
            * Challenge me to Staples Center

## BikeShareSystem

Representation of a GBFS bike share system. One instance per bike share system. (Metro, etc.)

Has configuration

* Max time for lowest-cost ride (human-picked)
* Min time (human-picked)
* Preferred abbreviations for long station/location names

## Challenger

Picks a route within a BikeShareSystem.

### Criteria

* Ridable within reasonable rate (i.e., < 30 min on Metro Bike Share)
* Long enough to have fun (i.e., > 10 min)
* Stays within one BikeShareSystem
* Stays within a geographic area of interest
