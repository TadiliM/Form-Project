# MVP — Form-Project

## Objectif

Permettre à un utilisateur de créer des formulaires personnalisés, de les partager, 
et de consulter les réponses collectées via un dashboard — avec un modèle freemium 
limitant certaines fonctionnalités aux abonnés Premium.

## Périmètre du MVP

### Inclus

- Inscription / connexion utilisateur (JWT)
- Création de formulaire avec champs de type texte, choix multiple, numérique
- Remplissage de formulaire par un répondant (sans compte requis)
- Visualisation des réponses par le créateur du formulaire
- Passage à l'abonnement Premium via Stripe (paiement récurrent mensuel)
- Restriction de certaines fonctionnalités (ex: nombre de formulaires) au plan Free

### Exclu du MVP (évolutions futures)

- Authentification Google OAuth
- Export des réponses (CSV/PDF)
- Champs de formulaire avancés (upload de fichier, date, signature)
- Notifications email à chaque nouvelle réponse
- Portail client Stripe pour gestion autonome de l'abonnement

## Critères de succès

- Un utilisateur peut créer un compte, créer un formulaire, le partager, recevoir 
  une réponse, et la consulter — de bout en bout, sans erreur.
- Un paiement Stripe test complet met à jour correctement le `PlanType` de l'utilisateur.