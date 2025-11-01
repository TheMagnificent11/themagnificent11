---
layout: default
title: Home
---

# Hi there 👋

I'm a .Net engineer based in Brisbane, Australia.

I've been working in .Net since I graduated from the University of Queensland in 2007.

I am currently employed by Workday (I work on the Adaptive Planning product).

I'm a father of two, partner to one.

I enjoy watching football (soccer), but enjoy the analysis of games more than actually watching the games (maybe that's the engineer in me).

I'm a Manchester United fan...Glazers Out!

I mainly listen to trance music, with Super8 & Tab being my favourite artist.

I also listen to rock. nuMetal is probably my favourite rock genre with Papa Roach being my favourite artist.

My GitHub username, `TheMagnificent11`, is play on words with the old "The Magnificent Seven" movie and my favourite football player at the time I signed up to GitHub, Ryan Giggs, who wore the number 11 shirt for Manchester United.

My main software engineering interests include domain-driven design and observability.

.Net Aspire looks interesting and I am enjoying playing-around with it.

Currently, I'm trying to get my head around [OpenTelementry.Net](https://github.com/open-telemetry/opentelemetry-dotnet) and how to integrate it with Grafana and Loki.

- 🔭 I'm currently working on [Lewee](https://github.com/TheMagnificent11/lewee)
- 🌱 I'm currently learning how to push traces and metrics to from .Net applications to Grafana.
- 👯 I'm looking to collaborate on [Lewee](https://github.com/TheMagnificent11/lewee).
- 🤔 I'm looking for help using .Net Aspire to deploy applications to Azure Container Apps while using Azure AD B2C for authentication and Azure Key Vault for secret management.
- 💬 Ask me about domain-driven design or football (soccer).
- 📫 How to reach me:
  - [Bluesky](https://bsky.app/profile/sajilicious.bsky.social)
  - [Mastodon](https://hachyderm.io/@sajilicious)
  - [LinkedIn](https://www.linkedin.com/in/saji-weerasingham/)
  - [Twitter/X](https://twitter.com/sajilicous), but I've stopped using it
- 😄 Pronouns: he/him
- ⚡ Fun fact
  - My surname, Weerasingham, has a typo.  It should start with "V".
  - In Sri Lanka, where I was born, there are two main languages, Singhalese and Tamil.
  - The Tamil spelling is "Veerasingham" and Singhalese spelling is "Weerasinghe", so mine is a mix of both because the family friend that registered my dad's birth was Singhalese and he made a mistake, but my dad decided to keep the incorrect spelling.

## Blog Posts

{% for post in site.posts %}
- [{{ post.title }}]({{ post.url | relative_url }}) - {{ post.date | date: "%B %d, %Y" }}
{% endfor %}

## Articles

- [Domain event dispatching using the outbox pattern with Entity Framework](/pages/2025-04-06_outbox-domain-event-dispatching-with-ef.html)
