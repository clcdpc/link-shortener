# README #

## What is this repository for?

* This project provides a link shortening service for CLC use

## How do I get set up?

### Dependencies
* A SQL database named 'LinkShortener' with a schema created by the script in the 'Extras' folder
* An AD account with SQL access to the LinkShortener database and a strong, non-expiring password

## Deployment instructions
* Create HTTPS IIS Site
* Set the application pool Identity to the AD account credentials
* Set 'Start Mode' on the application pool to 'AlwaysRunning'
* Set 'Preload enabled' on the site to 'True'
* Adjust IIS Site to offer Negotiate as an authentication provider
* Publish the project to the IIS site

## Other Configuration
* To allow a user to manage ALL short links and not just their own, add them to the LinkShortener.dbo.Admins table in domain\username format