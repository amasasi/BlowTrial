﻿using BlowTrial.Helpers;
using BlowTrial.Infrastructure.Interfaces;
using BlowTrial.Models;
using BlowTrial.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using BlowTrial.Infrastructure.Extensions;
using System.Windows;
using BlowTrial.Domain.Outcomes;
using MvvmExtraLite.Helpers;
using BlowTrial.Domain.Tables;
using AutoMapper;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Threading;
using System.Windows.Media;
using StatsForAge.DataSets;

namespace BlowTrial.ViewModel
{
    public abstract class ParticipantViewModel : ParticipantListItemViewModel,IDataErrorInfo
    {
        #region Fields
        OutcomeAt28DaysSplitter _outcomeSplitter;
        DispatcherTimer _ageTimer;

        ObservableCollection<VaccineViewModel> _allVaccinesAvailable;
        #endregion

        #region Constructors
        public ParticipantViewModel(IRepository repository ,ParticipantProgressModel participant) : base(participant)
        {
            _outcomeSplitter = new OutcomeAt28DaysSplitter(participant.OutcomeAt28Days);
            SaveChanges = new RelayCommand(Save, CanSave);
            NewVaccineCmd = new RelayCommand(CreateNewVaccine, CanCreateNewVaccine);

            AttachCollections();

            _ageTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = IntervalToSameTime(participant.DateTimeBirth)
            };
            _ageTimer.Tick += OnAgeIncrementing;
            _ageTimer.Start();
        }

        #endregion

        #region Properties
        public ParticipantModel Participant
        {
            get { return _participant; }
        }

        public ObservableCollection<VaccineAdministeredViewModel> VaccinesAdministered { get; private set; } 

        public bool IsParticipantModelChanged { get; private set; }

        public bool IsVaccineAdminChanged { get; private set; }

        public override string DisplayName
        {
            get
            {
                return string.Format(Strings.ParticipantUpdateVM_DisplayName, _participant.Id);
            }
        }

        public StudyCentreModel StudyCentre
        {
            get
            {
                return _participant.StudyCentre;
            }
        }
        public string StudyCentreName
        {
            get
            {
                return _participant.StudyCentre.Name;
            }
        }

        public string MothersName
        {
            get
            {
                return _participant.MothersName;
            }
        }

        public Brush TextColour
        {
            get
            {
                return _participant.StudyCentre.TextColour;
            }
        }

        public Brush BackgroundColour
        {
            get
            {
                return _participant.StudyCentre.BackgroundColour;
            }
        }

        public int Id
        {
            get
            {
                return _participant.Id;
            }
        }

        public int CentreId
        {
            get
            {
                return _participant.CentreId;
            }
        }

        public string Name
        {
            get
            {
                return _participant.Name;
            }
 
            set
            {
                if (value == _participant.Name) { return; }
                _participant.Name = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("Name");
            }
        }

        public string PhoneNumber
        {
            get
            {
                return _participant.PhoneNumber;
            }
            set
            {
                if (value == _participant.PhoneNumber) { return; }
                _participant.PhoneNumber = value;
                NotifyPropertyChanged("PhoneNumber");
            }
        }

        public string Notes
        {
            get
            {
                return _participant.Notes;
            }
            set
            {
                if (value == _participant.Notes) { return; }
                _participant.Notes = value;
                NotifyPropertyChanged("Notes");
            }
        }

        public string PhoneMask
        {
            get { return _participant.StudyCentre.PhoneMask; }
        }

        public string TrialArm
        {
            get
            {
                return _participant.TrialArm;
            }
        }

        public string HospitalIdentifier
        {
            get
            {
                return _participant.HospitalIdentifier;
            }
            /*
            set
            {
                if (value == _participant.HospitalIdentifier) { return; }
                _participant.HospitalIdentifier = value;
                NotifyPropertyChanged("HospitalIdentifier");
            }
            */
        }

        public int AgeDays
        {
            get
            {
                return _participant.Age.Days;
            }
        }

        public string AgeDaysString
        {
            get
            {
                if (IsKnownDead == true)
                {
                    return Strings.NotApplicable;
                }
                if (AgeDays>28)
                {
                    return ">28"; //≥
                }
                return AgeDays.ToString();
            }
        }

        public string CGA
        {
            get
            {
                int cga = _participant.CGA.Days;
                return (cga/7).ToString() + '.' + (cga % 7);
            }
        }

        public string Gender
        {
            get
            {
                return _participant.Gender;
            }
        }
        public int AdmissionWeight
        {
            get
            {
                return _participant.AdmissionWeight;
            }
        }
        public DateTime DateTimeBirth
        {
            get { return _participant.DateTimeBirth; }
        }
        public DateTime DateTimeEnrolment
        {
            get { return _participant.RegisteredAt; }
        }
        public bool? BcgAdverse
        {
            get
            {
                return _participant.BcgAdverse;
            }
            set
            {
                if (value == _participant.BcgAdverse) { return; }
                _participant.BcgAdverse = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("BcgAdverse", "BcgAdverseDetail");
            }
        }
        public string BcgAdverseDetail
        {
            get
            {
                return _participant.BcgAdverseDetail;
            }
            set
            {
                if (value == _participant.BcgAdverseDetail) { return; }
                _participant.BcgAdverseDetail = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("BcgAdverseDetail");
            }
        }
        public bool? BcgPapule
        {
            get
            {
                return _participant.BcgPapule;
            }
            set
            {
                if (value == _participant.BcgPapule) { return; }
                _participant.BcgPapule = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("BcgPapule");
            }
        }
        public int? LastContactWeight
        {
            get
            {
                return _participant.LastContactWeight;
            }
            set
            {
                if (value == _participant.LastContactWeight) { return; }
                _participant.LastContactWeight = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("LastContactWeight", "LastWeightDate");
                CalculateWtCentile();
            }
        }
        public DateTime? LastWeightDate
        {
            get
            {
                return _participant.LastWeightDate;
            }
            set
            {
                if (value == _participant.LastWeightDate) { return; }
                _participant.LastWeightDate = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("LastWeightDate");
                CalculateWtCentile();
            }
        }
        
        public CauseOfDeathOption CauseOfDeath
        {
            get
            {
                return _participant.CauseOfDeath;
            }
            set
            {
                if (value == _participant.CauseOfDeath) { return; }
                _participant.CauseOfDeath = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("CauseOfDeath", "OtherCauseOfDeathDetail");
            }
        }

        public DateTime? DischargeDate
        {
            get
            {
                return _participant.DischargeDate;
            }
            set
            {
                if (_participant.DischargeDate == value) { return; }
                _participant.DischargeDate = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("DischargeDate", "DischargeTime", "DischargeOrEnrolment");
            }
        }
        public TimeSpan? DischargeTime
        {
            get { return _participant.DischargeTime; }
            set
            {
                if (_participant.DischargeTime == value) { return; }
                _participant.DischargeTime = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("DischargeTime", "DischargeDate");
            }
        }

        public DateTime? DeathOrLastContactDate
        {
            get
            {
                return _participant.DeathOrLastContactDate;
            }
            set
            {
                if (_participant.DeathOrLastContactDate == value) { return; }
                _participant.DeathOrLastContactDate = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("DeathOrLastContactDate", "DeathOrLastContactTime", "DeathLastContactTodayOr28", "LastWeightDate");
            }
        }
        public TimeSpan? DeathOrLastContactTime
        {
            get { return _participant.DeathOrLastContactTime; }
            set
            {
                if (_participant.DeathOrLastContactTime == value) { return; }
                _participant.DeathOrLastContactTime = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("DeathOrLastContactTime", "DeathOrLastContactDate");
            }
        }
        public string DeathOrLastContactLabel
        {
            get
            {
                switch (_participant.IsKnownDead)
                {
                    case null:
                        return Strings.ParticipantUpdateView_Label_LastContactDateTime.ToLabelFormat();
                    case true:
                        return Strings.ParticipantUpdateView_Label_DeathDateTime.ToLabelFormat();
                    default: //false
                        return null;
                }
            }
        }
        public bool? IsKnownDead
        {
            get
            {
                return _participant.IsKnownDead;
            }
        }
        public string BcgPapuleLabel
        {
            get
            {
                switch (OutcomeAt28orDischarge)
                {
                    case OutcomeAt28DaysOption.DiedInHospitalBefore28Days:
                        return (Strings.ParticipantUpdateView_Label_BcgInduration + ' ' + Strings.ParticipantUpdateView_Label_Death).ToLabelFormat();
                    case OutcomeAt28DaysOption.InpatientAt28Days:
                        return (Strings.ParticipantUpdateView_Label_BcgInduration + ' ' + Strings.ParticipantUpdateView_Label_28days).ToLabelFormat();
                    case OutcomeAt28DaysOption.DischargedBefore28Days:
                        return (Strings.ParticipantUpdateView_Label_BcgInduration + ' ' + Strings.ParticipantUpdateView_Label_Discharge).ToLabelFormat();
                    default:
                        return null;
                }
            }
        }
        public string WeightLabel
        {
            get 
            {
                switch (OutcomeAt28orDischarge)
                {
                    case OutcomeAt28DaysOption.DiedInHospitalBefore28Days:
                        return (Strings.ParticipantUpdateView_Label_Weight + ' ' + Strings.ParticipantUpdateView_Label_Death).ToLabelFormat();
                    case OutcomeAt28DaysOption.InpatientAt28Days:
                        return (Strings.ParticipantUpdateView_Label_Weight + ' ' + Strings.ParticipantUpdateView_Label_28days).ToLabelFormat();
                    default:
                        if (OutcomeAt28orDischarge!=OutcomeAt28DaysOption.Missing)
                        {
                            return (Strings.ParticipantUpdateView_Label_Weight + ' ' + Strings.ParticipantUpdateView_Label_Discharge).ToLabelFormat();
                        }
                        return null;
                }
            }
        }
        public string OtherCauseOfDeathDetail
        {
            get
            {
                return _participant.OtherCauseOfDeathDetail;
            }
            set
            {
                if (value == _participant.OtherCauseOfDeathDetail) { return; }
                _participant.OtherCauseOfDeathDetail = value;
                IsParticipantModelChanged = true;
                NotifyPropertyChanged("OtherCauseOfDeathDetail");
            }
        }
        public bool DischargedBy28Days
        {
            get 
            {
                return _participant.OutcomeAt28Days >= OutcomeAt28DaysOption.DischargedBefore28Days;
            }
        }

        public OutcomeAt28DaysOption OutcomeAt28orDischarge
        {
            get
            {
                return _outcomeSplitter.OutcomeAt28orDischarge;
            }
            set
            {
                _outcomeSplitter.OutcomeAt28orDischarge = value;
                if (_outcomeSplitter.OutcomeAt28orDischarge == _participant.OutcomeAt28Days) { return; }
                OutcomeAt28Days = _outcomeSplitter.OutcomeAt28Days;
                if (OutcomeAt28Days < OutcomeAt28DaysOption.DischargedBefore28Days)
                {
                    _participant.DischargeDate = null;
                    _participant.DischargeTime = null;
                    _outcomeSplitter.PostDischargeOutcomeKnown = null;
                    _outcomeSplitter.DiedAfterDischarge = null;
                }
                NotifyPropertyChanged("DischargeDate", "DischargeTime", "OutcomeAt28orDischarge", "BcgPapuleLabel", "PostDischargeOutcomeKnown", "DiedAfterDischarge");
            }
        }
        public OutcomeAt28DaysOption OutcomeAt28Days
        {
            get 
            {
                return _participant.OutcomeAt28Days;
            }
            private set 
            {
                if (value == _participant.OutcomeAt28Days) { return; }
                _participant.OutcomeAt28Days = value;
                IsParticipantModelChanged = true;
                if (!IsDeathOrLastContactRequired) 
                {
                    _participant.DeathOrLastContactDate = null;
                    _participant.DeathOrLastContactTime = null;
                    _participant.CauseOfDeath = CauseOfDeathOption.Missing;
                }
                NotifyPropertyChanged("OutcomeAt28Days", "DischargedBy28Days", "IsKnownDead", "CauseOfDeath","DeathOrLastContactLabel", "WeightLabel", "IsDeathOrLastContactRequired", "DeathOrLastContactDate", "DeathOrLastContactTime");
            }
        }
        public bool? PostDischargeOutcomeKnown
        {
            get 
            {
                return _outcomeSplitter.PostDischargeOutcomeKnown;
            }
            set
            {
                if (value == _outcomeSplitter.PostDischargeOutcomeKnown) { return; }
                _outcomeSplitter.PostDischargeOutcomeKnown = value;
                NotifyPropertyChanged("PostDischargeOutcomeKnown");
                OutcomeAt28Days = _outcomeSplitter.OutcomeAt28Days;
                if (_outcomeSplitter.PostDischargeFieldsComplete)
                {
                    NotifyPropertyChanged("DiedAfterDischarge");// to remove validation messages
                }
            }
        }
        
        public bool? DiedAfterDischarge
        {
            get
            {
                return _outcomeSplitter.DiedAfterDischarge;
            }
            set
            {
                if (value == _outcomeSplitter.DiedAfterDischarge) { return; }
                _outcomeSplitter.DiedAfterDischarge = value;
                NotifyPropertyChanged("DiedAfterDischarge", "CauseOfDeath", "DeathOrLastContactTime", "DeathOrLastContactDate");
                OutcomeAt28Days = _outcomeSplitter.OutcomeAt28Days;
                if (_outcomeSplitter.PostDischargeFieldsComplete)
                {
                    NotifyPropertyChanged("PostDischargeOutcomeKnown");// to remove validation messages
                }
            }
        }

        public bool IsDeathOrLastContactRequired
        {
            get
            {
                return IsKnownDead == true || PostDischargeOutcomeKnown == false;
            }
        }
        public DateTime DischargeOrEnrolment
        {
            get
            {
                return DischargeDate
                    ?? DateTimeEnrolment;
            }
        }
        public DateTime DeathLastContactTodayOr28
        {
            get
            {
                return DeathOrLastContactDate
                    ?? TodayOr28;
            }
        }
        string _newVaccineName;
        public string NewVaccineName 
        {
            get {return _newVaccineName;}
            set 
            {
                string newVal = value.Trim();
                if (_newVaccineName == newVal) { return; }
                _newVaccineName = newVal;
                NotifyPropertyChanged ("NewVaccineName");
            }
        }

        public DateTime TodayOr28 // such a silly property is for the upper bound of the datepickers
        {
            get
            {
                return new DateTime[]{DateTime.Today, _participant.Becomes28On}.Min(d=>d);
            }
        }
        private string _wtForAgeCentile;
        public string WtForAgeCentile
        {
            get
            {
                return _wtForAgeCentile;
            }
            private set
            {
                if (value == _wtForAgeCentile) { return; }
                _wtForAgeCentile = value;
                NotifyPropertyChanged("WtForAgeCentile");
            }
        }
        public string DataRequired
        {
            get
            {
                return DetailsDictionary.GetDetails(_participant.DataRequired);
            }
        }
        #endregion

        #region Listbox options
        IEnumerable<KeyValuePair<CauseOfDeathOption, string>> _CauseOfDeathOptions;
        public IEnumerable<KeyValuePair<CauseOfDeathOption, string>> CauseOfDeathOptions
        {
            get
            {
                return _CauseOfDeathOptions 
                    ?? (_CauseOfDeathOptions = EnumToListOptions<CauseOfDeathOption>());
            }
        }
        IEnumerable<SelectableItem<OutcomeAt28DaysOption>> _outcomeAt28orDischargeOptions;
        public IEnumerable<SelectableItem<OutcomeAt28DaysOption>> OutcomeAt28orDischargeOptions
        {
            get
            {
                if (_outcomeAt28orDischargeOptions==null)
                {
                    bool is28daysOld = AgeDays >= 28;
                    _outcomeAt28orDischargeOptions = (from o in EnumToListOptions<OutcomeAt28DaysOption>()
                                                      where o.Key <= OutcomeAt28DaysOption.DischargedBefore28Days
                                                      select new SelectableItem<OutcomeAt28DaysOption>(o.Key, o.Value)
                                                      {
                                                        IsEnabled = o.Key != OutcomeAt28DaysOption.InpatientAt28Days || is28daysOld
                                                      }).ToArray();
                }
                return _outcomeAt28orDischargeOptions;
            }
        }

        ObservableCollection<VaccineViewModel> AllVaccinesAvailable
        {
            get
            {
                if (_allVaccinesAvailable == null)
                {
                    var allVaccines = _repository.Vaccines.ToList()
                        .Select(v=>new VaccineViewModel(v)).ToList();
                    allVaccines.Insert(0, new VaccineViewModel(null));
                    _allVaccinesAvailable = new ObservableCollection<VaccineViewModel>(allVaccines);
                }
                return _allVaccinesAvailable;
            }
        }
        KeyValuePair<bool?, string>[] _requiredBoolOptions;
        public KeyValuePair<bool?, string>[] RequiredBoolOptions
        {
            get
            {
                return _requiredBoolOptions ?? (_requiredBoolOptions = CreateBoolPairs());
            }
        }
        #endregion

        #region IDataErrorInfo Members

        string IDataErrorInfo.Error { get { return null; } }

        string IDataErrorInfo.this[string propertyName]
        {
            get 
            { 
                string returnVal = this.GetValidationError(propertyName);
                CommandManager.InvalidateRequerySuggested();
                return returnVal;
            }
        }

        #endregion // IDataErrorInfo Members

        #region Methods
        void CalculateWtCentile()
        {
            if (LastWeightDate == null || LastContactWeight==null)
            {
                WtForAgeCentile = string.Empty;
            }
            else
            {
                var weightData = new UKWeightData(); 
                //note addDays is just to get roughly the mean time of weight
                WtForAgeCentile = string.Format(Strings.NewPatientVM_Centile,
                    weightData.CumSnormForAge((double)LastContactWeight.Value / 1000, (LastWeightDate.Value.AddDays(0.5) - DateTimeBirth.Date).TotalDays, _participant.IsMale, _participant.GestAgeBirth));
            }
        }

        void AttachCollections()
        {
            if (_participant.VaccineModelsAdministered == null)
            {
                _participant.VaccineModelsAdministered = Mapper.Map<List<VaccineAdministeredModel>>((from r in _repository.VaccinesAdministered
                         where r.ParticipantId == _participant.Id
                         select r).ToList());
                
            }
            if (_participant.StudyCentre == null)
            {
                _participant.StudyCentre = _repository.LocalStudyCentres.First(s => s.Id == _participant.CentreId);
            }
            if (VaccinesAdministered != null)
            {
                VaccinesAdministered.Clear(); // remove event handlers
            }
            else
            {
                VaccinesAdministered = new ObservableCollection<VaccineAdministeredViewModel>();
                VaccinesAdministered.CollectionChanged += VaccinesAdministered_CollectionChanged;
            }

            foreach (VaccineAdministeredModel model in _participant.VaccineModelsAdministered)
            {
                VaccineAdministeredViewModel vm = new VaccineAdministeredViewModel (model, AllVaccinesAvailable);
                VaccinesAdministered.Add(vm);
            }
            VaccinesAdministered.Add(NewVaccineAdministeredViewModel());
        }
        VaccineAdministeredViewModel NewVaccineAdministeredViewModel()
        {
            var returnVar = new VaccineAdministeredViewModel(
                new VaccineAdministeredModel
                {
                    AdministeredTo = _participant
                }
                , AllVaccinesAvailable) { AllowEmptyRecord = true };
            returnVar.PropertyChanged += NewVaccineAdminVm_PropertyChanged;
            return returnVar;
        }
        private void VaccinesAdministered_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (VaccineAdministeredViewModel v in e.OldItems)
                {
                    v.PropertyChanged -= NewVaccineAdminVm_PropertyChanged;
                    v.PropertyChanged -= VaccineAdminVm_PropertyChanged;

                    var item = _participant.VaccineModelsAdministered.FirstOrDefault(va=>va == v.VaccineAdministeredModel);
                    if (item!=null)
                    {
                        _participant.VaccineModelsAdministered.Remove(item);
                        if (item.VaccineGiven != null)
                        {
                            AllVaccinesAvailable.First(a => a.Vaccine == item.VaccineGiven).IsGivenToThisPatient = false;
                        }
                        IsVaccineAdminChanged = true;
                    }
                }
            }
            if (e.NewItems != null)
            {
                foreach (VaccineAdministeredViewModel v in e.NewItems)
                {
                    v.PropertyChanged += VaccineAdminVm_PropertyChanged;
                }
            }
        }
        void VaccineAdminVm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "AdministeredAtDate" || e.PropertyName == "AdministeredAtTime" || e.PropertyName == "SelectedVaccine")
            {
                IsVaccineAdminChanged = true;
            }
        }
        private void NewVaccineAdminVm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "AdministeredAtDate" || e.PropertyName == "AdministeredAtTime" || e.PropertyName == "SelectedVaccine")
            {
                var newVaccineAdminVm = (VaccineAdministeredViewModel)sender;
                newVaccineAdminVm.AllowEmptyRecord = newVaccineAdminVm.IsEmpty;
                if (!newVaccineAdminVm.IsEmpty && newVaccineAdminVm.IsValid())
                {
                    newVaccineAdminVm.AllowEmptyRecord = false;
                    _participant.VaccineModelsAdministered.Add(newVaccineAdminVm.VaccineAdministeredModel);
                    newVaccineAdminVm.PropertyChanged -= NewVaccineAdminVm_PropertyChanged;
                    VaccinesAdministered.Add(NewVaccineAdministeredViewModel());
                }
            }
        }

        #endregion

        #region ICommands
        public ICommand SaveChanges { get; private set; }
        bool CanSave(object param)
        {
            return (IsParticipantModelChanged && WasValidOnLastNotify) || (IsVaccineAdminChanged  && VaccinesAdministered.All(v => v.IsValid()));
        }
        void Save(object param)
        {
            if (IsParticipantModelChanged)
            {
                _repository.UpdateParticipant(
                    id : _participant.Id,
                    name : _participant.Name,
                    phoneNumber : _participant.PhoneNumber,
                    causeOfDeath : _participant.CauseOfDeath,
                    otherCauseOfDeathDetail: _participant.OtherCauseOfDeathDetail,
                    bcgAdverse : _participant.BcgAdverse,
                    bcgAdverseDetail: _participant.BcgAdverseDetail,
                    bcgPapule : _participant.BcgPapule,
                    lastContactWeight : _participant.LastContactWeight,
                    lastWeightDate : _participant.LastWeightDate,
                    dischargeDateTime : _participant.DischargeDateTime,
                    deathOrLastContactDateTime : _participant.DeathOrLastContactDateTime,
                    outcomeAt28Days : _participant.OutcomeAt28Days,
                    notes : _participant.Notes
                );
                IsParticipantModelChanged = false;
            }
            if (IsVaccineAdminChanged)
            {
                _repository.AddOrUpdateVaccinesFor(_participant.Id, _participant.VaccineModelsAdministered.Select
                    (v => new VaccineAdministered 
                    {
                        Id = v.Id,
                        AdministeredAt = v.AdministeredAtDateTime.Value,
                        VaccineId = v.VaccineGiven.Id
                    }));
                IsVaccineAdminChanged = false;
            }
        }
        public ICommand NewVaccineCmd { get; private set; }
        bool CanCreateNewVaccine(object param)
        {
            return !string.IsNullOrWhiteSpace(NewVaccineName) && GetValidationError("NewVaccineName") == null;
        }
        void CreateNewVaccine(object param)
        {
            Vaccine vax = new Vaccine { Name = NewVaccineName };
            _repository.Add(vax);
            AllVaccinesAvailable.Add(new VaccineViewModel(vax));
        }
        #endregion

        #region Window Event Handlers

        internal void OnClosingWindow(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = !OkToProceed();
        }

        #endregion

        #region Methods

        public bool OkToProceed()
        {
            if (IsParticipantModelChanged || IsVaccineAdminChanged)
            {
                string title;
                string msg;
                MessageBoxButton buttonOptions;
                if (IsValid())
                {
                    title = Strings.ParticipantUpdateVM_Confirm_SaveChanges_Title;
                    msg = Strings.ParticipantUpdateVM_Confirm_SaveChanges;
                    buttonOptions = MessageBoxButton.YesNoCancel;
                }
                else
                {
                    title = Strings.ParticipantUpdateVM_Confirm_Close_Title;
                    msg = Strings.ParticipantUpdateVM_Confirm_Close;
                    buttonOptions = MessageBoxButton.OKCancel;
                }
                MessageBoxResult result = MessageBox.Show(
                    msg,
                    title,
                    buttonOptions,
                    MessageBoxImage.Question);
                switch (result)
                {
                    case MessageBoxResult.Yes:
                        Save(null);
                        break;
                    case MessageBoxResult.Cancel:
                        return false;
                    default: //OK in Proceed ? OK/Cancel and No In Close without saving? yes/no/cancel
                        CancelChanges();
                        break;
                }
            }
            // Yes, OK or No
            return true; 
        }

        void CancelChanges()
        {
            string oldName = _participant.Name;
            _participant = Mapper.Map<ParticipantModel>(
                            _repository.Participants.Include("VaccinesAdministered").Include("VaccinesAdministered.VaccineGiven")
                                .First(p => p.Id == _participant.Id));
            IsParticipantModelChanged = IsVaccineAdminChanged = false;
            _outcomeSplitter = new OutcomeAt28DaysSplitter(_participant.OutcomeAt28Days);
            AttachCollections();
            if (oldName != _participant.Name)
            {
                NotifyPropertyChanged("Name");
            }
        }

        #endregion

        #region AgeTimer

        static TimeSpan IntervalToSameTime(DateTime orginalDateTime)
        {
            var returnVar = DateTime.Now.TimeOfDay - orginalDateTime.TimeOfDay;
            if (returnVar.TotalHours < 0)
            {
                returnVar += TimeSpan.FromDays(1);
            }
            return returnVar;
        }

        void OnAgeIncrementing(object sender, EventArgs e)
        {
            NotifyPropertyChanged("AgeDays", "CGA", "TodayOr28", "DeathLastContactTodayOr28", "DataRequired");
            OutcomeAt28orDischargeOptions.First(o => o.Key == OutcomeAt28DaysOption.InpatientAt28Days).IsEnabled = AgeDays >= 28;
            _ageTimer.Interval = IntervalToSameTime(_participant.DateTimeBirth);
        }

        #endregion

        #region Validation

        /// <summary>
        /// Returns true if this object has no validation errors.
        /// </summary>
        public override bool IsValid()
        {
            bool returnVal = _participant.IsValid() && !ValidatedProperties.Any(v => this.GetValidationError(v) != null);
            CommandManager.InvalidateRequerySuggested();
            return returnVal;
            
        }

        static readonly string[] ValidatedProperties = 
        { 
            "DiedAfterDischarge"
        };

        public string GetValidationError(string propertyName)
        {
            switch (propertyName)
            {
                case "OutcomeAt28orDischarge":
                    propertyName = "OutcomeAt28Days";
                    break;
                case "DiedAfterDischarge":
                case "PostDischargeOutcomeKnown":
                    return ValidatePostDischargeOutcomes();
                case "NewVaccineName":
                    return ValidateNewVaccineName();
            }
            return ((IDataErrorInfo)_participant)[propertyName];
        }
        string ValidateNewVaccineName()
        {
            if (!string.IsNullOrEmpty(NewVaccineName) && _repository.Vaccines.Any(v => v.Name == NewVaccineName))
            {
                return Strings.CreateNewVaccine_Duplicate;
            }
            return null;
        }
        string ValidatePostDischargeOutcomes()
        {
            if (PostDischargeOutcomeKnown.HasValue && !DiedAfterDischarge.HasValue)
            {
                return string.Format(Strings.ParticipantUpdateVM_Error_DiedAfterDischargeRequired,
                    PostDischargeOutcomeKnown.Value
                        ?Strings.ParticipantUpdateVM_Error_Known
                        :Strings.ParticipantUpdateVM_Error_Likely);
            }
            if (DiedAfterDischarge.HasValue && !PostDischargeOutcomeKnown.HasValue)
            {
                return string.Format(Strings.ParticipantUpdateVM_Error_PostDischargeOutcomeRequired,
                    DiedAfterDischarge.Value
                        ? Strings.ParticipantUpdateVM_Error_Death
                        : Strings.ParticipantUpdateVM_Error_Survival);
            }
            return null;
        }

        #endregion //Validation

        #region finaliser
        ~ParticipantViewModel()
        {
            _ageTimer.Stop();
            _ageTimer.Tick -= OnAgeIncrementing;
        }
        #endregion
    }
}
