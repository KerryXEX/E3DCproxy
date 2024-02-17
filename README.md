# E3DCproxy
A proxy server to access E3DC status and history data from the internet.

This library provides read-only access to most commonly used status and data from an E3DC system and does not fully implement the wohle RSCP api.

# Setup
The proxy server can run natively as a dotnet web server on Windows, Mac, Linux or in a Docker container created with the provided Dockerfile.
Access from the internet needs to be setup via a reverse proxy or simple port forwarding in your router.
The standard exposed port is 5033 for http protocol.

A reverse proxy with SSL encyption is highly recommended!

# Configuration
To configure access to your E3DC power system, the following environment variables need to be set either in 'appsettings.json' or the Docker environment:
- Local IP address of the E3DC system (192.168.x.x)
- Your login name (email) for the E3DC portal
- Your login password for the E3DC portal
- The RSCP password set in the E3DC power system

# API Requests
The follwoing api requests are implemented and return JSON strings:
- "/": returns the E3DC server data object configuration data as SunServer object
- "/canConnect": returns bool if E3DC RSCP connect is successful
- "/getStates": returns current E3DC states as SunState object
- "/getHistSumStates": returns a list of SunState objects with sum values for today, yesterday, 7-days, 30-days, 90-days, 360-days
- "/getHistory?days=1&interval=900": returns a list of Sunstate objects with sums for the requested timespan (# of days) and interval (# of seconds)
- "/getBatteries": returns config data for installed batteries as List of BatteryData object

# API Request Security
API requests must submit the RSCP password as API-Key authorization with name "X-Api-Key" added to the request header.

# Frontend App
The corresponding frontend app "SunStateApp" is published on the Apple AppStore and Google Playstore.

# License
This library is licensed under GNU GPL v3. I'd be happy if you have requests for further API call implementations or other suggestions!

# Thanks
Thanks to [@am-e3dc](https://github.com/Spontifixus/am-e3dc) (Spontifixus) for the dotnet implementention of the RSCP protocol layer and also to [@rxhan](https://github.com/rxhan) and his RSCPGui project which helped alot in finding out about various RSCP calls.

  


