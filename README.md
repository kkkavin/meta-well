# MetaWell: VR-Based Mental Wellness & Phobia Treatment Platform

> **Step Into Wellness. Heal Beyond Reality.**

MetaWell is an innovative, immersive Virtual Reality (XR) platform engineered to make therapeutic mental healthcare affordable, scalable, and entirely stigma-free. Built using the **Unity 3D Engine** and targeting standalone **Meta Quest VR Headsets**, the platform combines predictive conversational AI, multi-sensory open-gaze mindfulness models, and controlled, progression-based exposure therapy protocols within a unified biophilic digital pavilion.

---

## 📌 Problem Statement
Traditional mental health frameworks face immense systemic challenges:
* **The Care Disparity:** 75% of mental health disorders manifest before age 24, yet professional psychiatric access is globally scarce (e.g., India has only 1 psychiatrist per 250,000 people).
* **Socio-Economic Barriers:** Out-of-pocket therapeutic care incurs significant financial strain, and rural communities lack modernized physical clinical spaces.
* **Societal Stigma:** Ingrained social barriers frequently block individuals from seeking early psychological assistance or entering physical clinics.
* **Exposure Framework Complexity:** Orachastrating traditional real-world exposure therapy for phobias is highly unstable, difficult to precisely predict, control, and economically execute.

---

## 💡 The Solution: MetaWell Pavilion Ecosystem
MetaWell unifies medically grounded frameworks with high-fidelity spatial design, launching the user into a peaceful, biophilic pavilion. By organizing the distinct **AI therapy, breathwork, and phobia tracking** modules into an optimized multi-scene workflow, the platform delivers a structured therapeutic experience that preserves high frame rates and user immersion across every stage of treatment.

### Core Modular Components:
1. **🤖 AI Therapist Companion**
   * A sleek, conversational AI conversational interface providing localized, secure, and immediate emotional validation. It lowers the barrier of entry for individuals experiencing clinical social anxiety or social fear.
2. **🫁 Multi-Sensory Guided Breathing (Open-Gaze Method)**
   * Built specifically to leverage **visual-anchor tracking** which requires users to keep their eyes open. This design prevents the typical disorientation, motion sickness, or spatial isolation felt when closing eyes inside an XR headset. 
   * Includes 4 rhythmic interactive environments (e.g., *Box Breathing Visualizer*, *Shoreline Tide Breath*) where environmental parameters like lighting coves and sea tides dynamically scale in direct cadence with breathing loops.
3. **🏙️ Height Phobia (Acrophobia) Exposure Therapy**
   * A progressive, safety-controlled therapeutic environment spanning Levels 1 to 4. 
   * Features reinforced glass-floor observation zones that gradually elevate simulated real-time heights to help users build spatial cognitive resilience within a risk-free metaverse zone.

---

## 🛠️ Technical Stack & Frameworks

* **Game Engine:** Unity 3D Engine
* **IDE:** Visual Studio Code
* **Language:** C# (C-Sharp)
* **XR Pipeline:** OpenXR Specification / Oculus XR Plug-in Management Framework
* **Target Hardware:** Standalone Android Package (.apk) optimized for Meta Quest Devices
* **AI Support:** Firebase Platform (Secure user metrics & mood analytics tracking)
* **3D Design Assets:** Blender (Custom low-poly biophilic elements, warm wood panel paths, and water droplets)

---

## ⚙️ Architecture & Implementation Notes

### Multi-Scene Comfort Optimization
To protect user equilibrium and prevent any sudden simulation discomfort during therapeutic sessions, MetaWell utilizes a multi-scene architecture engineered for performance and comfort. Moving between the primary pavilion hub, individual mindfulness coves, and progressive exposure tiers employs a smooth camera-fading mask interpolation coupled with gradual audio transitions. This approach bridges treatment settings gracefully, avoiding jarring environment loads or vertical visual shifts.

### XR Rig Positioning & Stability
The platform’s tracking and locomotion systems are designed around a strict spatial stabilization protocol. The primary `XR Origin` configuration maintains fixed calibration references across scene transitions, eliminating tracking alignment drift or sudden camera displacement. This alignment ensures that patients remain safely grounded, secure, and fully immersed in their therapy environment as they step from one specialized module into the next.

---

## 🚀 Setup & Installation Guide

Follow these sequential steps to configure the development environment, integrate third-party AI assets, and deploy the application.

### 📦 1. Repository & Project Initialization

* **Clone the Project Repository:**
  git clone https://github.com/kkkavin/meta-well.git

* **Open via Unity Hub:**
  * Launch Unity Hub, click Add Project, and choose the cloned project root folder.
  * Recommended Editor Version: Unity 6000.3.10f1 LTS or newer.

### 🌐 2. XR Plug-in & Platform Configuration

* **Verify XR Pipelines:**
  * Inside the Unity Editor, open Edit > Project Settings > XR Plug-in Management.
  * Navigate to the Android Settings Tab (represented by the Android robot icon).
  * Ensure either OpenXR or Oculus is enabled based on your deployment preferences.

### 🤖 3. ConvAI SDK Setup (AI Therapist Feature)

To activate the conversational capabilities within the interactive AITherapist scene, populate your credentials using the steps below:

#### Step A: Account Credentials
* Log into or create an account on the ConvAI Dashboard (https://convai.com).
* Navigate to your user profile settings page and copy your master API Key.
* In Unity, go to Edit > Project Settings > ConvAI SDK.
* Locate the Credentials segment and paste your copied key into the API field.

#### Step B: Character Assignment
* Go back to the ConvAI dashboard website and select Create Character.
* Define your custom virtual therapist parameters, save the profile, and copy the unique Character ID along with the assigned Character Name.
* In Unity's Project window, open the **AITherapist** scene.
* Inside the Hierarchy window, expand the primary ConvAI GameObject and select the child ConvAI Character GameObject.
* With it highlighted, look at the Identity section inside the Inspector window on the right side of the editor.
* Paste your copied Character ID and Character Name into their respective parameters.

### 🎮 4. Build and Deploy onto Meta Quest

* **Hardware Connection:**
  * Connect your Meta Quest hardware to your workstation via high-speed Link Cable or Wireless AirLink. (Verify that Developer Mode is toggled to ON within your Meta Horizon mobile app settings).
* **Compilation Process:**
  * Make sure that the **Main** scene is only loaded before building.
  * In Unity, click File > Build Profiles.
  * Switch your target development platform to Android or Meta Quest.
  * Verify that all required environment scenes are listed under the Scenes In Build list.
  * Click Build and Run.

---

## 🔮 Future Roadmap

* **Phobia Catalog Expansion:** Developing standalone spatial modules targeting Aerophobia (Fear of Flying) and Claustrophobia.
* **Secure Social Spaces:** Multiplayer VR support circles and safe training arenas for social anxiety.
* **Advanced PTSD Recovery tracks:** Introducing custom generative environments driven by predictive AI-powered mental health assessment models.

---

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for documentation details.
