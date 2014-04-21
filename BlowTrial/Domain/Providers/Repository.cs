﻿using BlowTrial.Domain.Interfaces;
using BlowTrial.Domain.Outcomes;
using BlowTrial.Domain.Tables;
using BlowTrial.Infrastructure.Interfaces;
using BlowTrial.Models;
using Ionic.Zip;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Data.Entity.Migrations;
using BlowTrial.Infrastructure.Exceptions;
using System.Data.Entity.Infrastructure;
using BlowTrial.Helpers;
using LinqKit;
using BlowTrial.Infrastructure;
using BlowTrial.Infrastructure.Randomising;
using BlowTrial.Migrations;

namespace BlowTrial.Domain.Providers
{
    public class Repository : IRepository
    {
        #region Constructors
        public Repository(Type iDataContextType) :this(TypeToConstructor(iDataContextType))
        {
            
#if DEBUG
            if (iDataContextType.GetInterface("ITrialDataContext")==null)
            {
                throw new ArgumentException("Argument iDataContextType must implement ITrialDataContext");
            }
#endif
        }
        public Repository(Func<ITrialDataContext> createContext)
        {
            _createContext = createContext;
            _dbContext = _createContext.Invoke(); //keep connection alive
        }
        static Func<ITrialDataContext> TypeToConstructor(Type iDataContextType)
        {
            ConstructorInfo typeConstructor = iDataContextType.GetConstructor(System.Type.EmptyTypes);
#if DEBUG
            if (typeConstructor == null)
            {
                throw new ArgumentException("iDataContextType must have a constructor which takes no arguments");
            }
#endif
            object[] _emptyObjects = new object[0];
            return delegate { return (ITrialDataContext)typeConstructor.Invoke(_emptyObjects); };
        }
        #endregion // Constructors

        #region Members
        private readonly Func<ITrialDataContext> _createContext;
        private ITrialDataContext _dbContext;
        private const string BakExtension = ".sdf"; //obviously change if using non-compact
        private List<string> _baksForgoingMigration;
        #endregion // Members

        #region EventHandlers
        public event EventHandler<ParticipantEventArgs> ParticipantAdded;
        public event EventHandler<ScreenedPatientEventArgs> ScreenedPatientAdded;
        public event EventHandler<ProtocolViolationEventArgs> ProtocolViolationAdded;
        public event EventHandler<ParticipantEventArgs> ParticipantUpdated;
        //public event EventHandler<ScreenedPatientEventArgs> ScreenedPatientUpdated;
        #endregion // EventHandlers

        #region Properties
        public Database Database
        {
            get { return _dbContext.Database; }
        }
        public IEnumerable<string> CloudDirectories
        {
            get;
            set;
        }

        public DbQuery<Vaccine> Vaccines
        {
            get
            {
                return _dbContext.Vaccines.AsNoTracking();
            }
        }
        public DbQuery<Participant> Participants
        {
            get
            {
                return _dbContext.Participants.AsNoTracking();
            }
        }
        public DbQuery<ScreenedPatient> ScreenedPatients
        {
            get
            {
                return _dbContext.ScreenedPatients.AsNoTracking();
            }
        }
        public DbQuery<VaccineAdministered> VaccinesAdministered
        {
            get
            {
                return _dbContext.VaccinesAdministered.AsNoTracking();
            }
        }
        public DbQuery<ProtocolViolation> ProtocolViolations
        {
            get
            {
                return _dbContext.ProtocolViolations.AsNoTracking();
            }
        }
        Dictionary<int, StudyCentreModel> _localStudyCentreDictionary;
        Dictionary<int, StudyCentreModel> LocalStudyCentreDictionary 
        {
            get 
            {
                if (_localStudyCentreDictionary == null)
                {
                    _localStudyCentreDictionary = (from s in _dbContext.StudyCentres 
                                                  select new StudyCentreModel
                                                    {
                                                        Id = s.Id,
                                                        Name = s.Name,
                                                        ArgbTextColour = s.ArgbTextColour,
                                                        ArgbBackgroundColour = s.ArgbBackgroundColour,
                                                        HospitalIdentifierMask = s.HospitalIdentifierMask,
                                                        PhoneMask = s.PhoneMask,
                                                        MaxIdForSite = s.MaxIdForSite,
                                                        DuplicateIdCheck = s.DuplicateIdCheck // needed for backup
                                                    }).ToDictionary(scm=>scm.Id);
                }
                return _localStudyCentreDictionary;
            }
        }

        public IEnumerable<StudyCentreModel> LocalStudyCentres
        {
            get { return LocalStudyCentreDictionary.Values; }
        }

        public StudyCentreModel FindStudyCentre(int studyCentreId)
        {
            return LocalStudyCentreDictionary[studyCentreId];
        }

        #endregion // Properties

        #region Methods
        public Participant FindParticipant(int participantId)
        {
            return _dbContext.Participants.Find(participantId);
        }
        public ProtocolViolation FindViolation(int violationId)
        {
            return _dbContext.ProtocolViolations.Find(violationId);
        }
        public Participant AddParticipant(
            string name,
            string mothersName,
            string hospitalIdentifier,
            int admissionWeight,
            double gestAgeBirth,
            DateTime dateTimeBirth,
            string AdmissionDiagnosis,
            string phoneNumber,
            bool isMale,
            bool? inborn,
            DateTime registeredAt,
            int centreId,
            int? multipleSiblingId,
            int? envelopeNumber = null)
        {
            Participant newParticipant = new Participant
            {
                Name = name,
                MothersName = mothersName,
                HospitalIdentifier = hospitalIdentifier.Trim(),
                AdmissionWeight = admissionWeight,
                GestAgeBirth = gestAgeBirth,
                DateTimeBirth = dateTimeBirth,
                AdmissionDiagnosis = AdmissionDiagnosis,
                PhoneNumber =  phoneNumber,
                IsMale = isMale,
                Inborn = inborn,
                RegisteredAt = registeredAt,
                RegisteringInvestigator = System.Threading.Thread.CurrentPrincipal.Identity.Name,
                CentreId = centreId,
                WasEnvelopeRandomised = envelopeNumber.HasValue,
                AppVersionAtEnrollment = App.CurrentAppVersion
            };
            if (multipleSiblingId.HasValue)
            {
                var multipleSibling = _dbContext.Participants.Find(multipleSiblingId.Value);
                if (multipleSibling == null)
                {
                    throw new ArgumentException("Participant Not Found", "multipleSiblingId");
                }
                if (multipleSibling.IsMale == newParticipant.IsMale)
                {
                    newParticipant.IsInterventionArm = multipleSibling.IsInterventionArm;
                    newParticipant.MultipleSiblingId = multipleSiblingId;
                    if (envelopeNumber.HasValue)
                    {
                        newParticipant.Id = (from p in _dbContext.Participants
                                             where p.Id > EnvelopeDetails.MaxEnvelopeNumber
                                             orderby p.Id descending
                                             select p.Id).FirstOrDefault();
                        if (newParticipant.Id == 0)
                        {
                            newParticipant.Id = EnvelopeDetails.MaxEnvelopeNumber;
                        }
                        newParticipant.Id += 1;
                    }
                    else
                    {
                        newParticipant.Id = GetNextId(_dbContext.Participants, centreId);
                        RandomisingEngine.ForceAllocationToArm(newParticipant, this);
                    }
                }
            }
            if (!newParticipant.MultipleSiblingId.HasValue)
            {
                if (envelopeNumber.HasValue)
                {
                    Envelope envelope = EnvelopeDetails.GetEnvelope(envelopeNumber.Value);
                    newParticipant.BlockNumber = envelope.BlockNumber;
                    newParticipant.BlockSize = envelope.BlockSize;
                    newParticipant.IsInterventionArm = envelope.IsInterventionArm;
                    newParticipant.Id = envelopeNumber.Value;
                }
                else
                {
                    newParticipant.Id = GetNextId(_dbContext.Participants, centreId);
                    RandomisingEngine.CreateAllocation(newParticipant, this);
                }
            }
            Add(newParticipant);
            return newParticipant;
        }
        void Add(Participant participant)
        {
            var centre = FindStudyCentre(participant.CentreId);
            if (participant.Id < centre.Id)
            {
                throw new DataKeyOutOfRangeException("participant id less than id for site");
            }
            else if (participant.Id > centre.MaxIdForSite)
            {
                throw new DataKeyOutOfRangeException("participant id greater than maximum allocations for site");
            }

            _dbContext.Participants.Add(participant);

            _dbContext.SaveChanges();
            if (this.ParticipantAdded != null)
            {
                this.ParticipantAdded(this, new ParticipantEventArgs(participant));
            }
        }
        public void Add(ScreenedPatient patient)
        {
            _dbContext.ScreenedPatients.Add(patient);
            patient.Id = GetNextId(_dbContext.ScreenedPatients, patient.CentreId);
            patient.AppVersionAtEnrollment = App.CurrentAppVersion;

            _dbContext.SaveChanges();
            if (this.ScreenedPatientAdded != null)
            {
                this.ScreenedPatientAdded(this, new ScreenedPatientEventArgs(patient));
            }
        }
        public void AddOrUpdate(IEnumerable<StudyCentre> centres)
        {
            foreach (StudyCentre s in centres)
            {
                _dbContext.StudyCentres.AddOrUpdate(s);
            }
            _dbContext.SaveChanges();
            _localStudyCentreDictionary = null;
        }
        public void AddOrUpdate(ProtocolViolation violation)
        {
            if (violation.ParticipantId == 0)
            {
                throw new ArgumentException("Participant Id must have a value");
            }
            if (violation.Id==0)
            { 
                int centreId = (from p in _dbContext.Participants
                                where p.Id == violation.ParticipantId
                                select p.CentreId).First();
                violation.Id = GetNextId(_dbContext.ProtocolViolations, centreId);
                violation.ReportingTimeLocal = DateTime.Now;
                violation.ReportingInvestigator = System.Threading.Thread.CurrentPrincipal.Identity.Name;
                _dbContext.ProtocolViolations.Add(violation);
                if (this.ProtocolViolationAdded != null)
                {
                    this.ProtocolViolationAdded(this, new ProtocolViolationEventArgs(violation));
                }
            }
            else
            {
                var entry = _dbContext.Entry<ProtocolViolation>(violation);

                if (entry.State == EntityState.Detached)
                {
                    ProtocolViolation attachedViol = _dbContext.ProtocolViolations.Local.FirstOrDefault(v => v.Id == violation.Id);
                    if (attachedViol == null)
                    {
                        entry.State = EntityState.Modified;
                    }
                    else
                    {
                        _dbContext.Entry(attachedViol).CurrentValues.SetValues(violation);
                    }
                }
            }
            _dbContext.SaveChanges();
        }
        const string BlockRandomisationViolation = "Alteration to data which would have affected randomisation:";
        static string GenderString(bool isMale)
        {
            return isMale ? "Male" : "Female";
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="isMale"></param>
        /// <param name="phoneNumber"></param>
        /// <param name="AdmissionDiagnosis"></param>
        /// <param name="admissionWeight"></param>
        /// <param name="dateTimeBirth"></param>
        /// <param name="gestAgeBirth"></param>
        /// <param name="hospitalIdentifier"></param>
        /// <param name="isInborn"></param>
        /// <param name="multipleSibblingId"></param>
        /// <param name="registeredAt"></param>
        /// <returns>Whether a protocol violation has been logged due to randomising criteria being altered</returns>
        public UpdateParticipantViolationType UpdateParticipant(int id,
            string name,
            bool isMale,
            string phoneNumber,
            string AdmissionDiagnosis,
            int admissionWeight,
            DateTime dateTimeBirth,
            double gestAgeBirth,
            string hospitalIdentifier,
            bool? isInborn,
            int? multipleSibblingId,
            DateTime registeredAt,
            bool isEnvelopeRandomising)
        {
            Participant participant = _dbContext.Participants.Find(id);
            var pv = new List<ProtocolViolation>();
            UpdateParticipantViolationType returnVar = UpdateParticipantViolationType.NoViolations;
            if (!RandomisingExtensions.IsSameRandomisingCategory(participant.IsMale, isMale, participant.AdmissionWeight, admissionWeight))
            {
                pv.Add(new ProtocolViolation
                {
                    Details = string.Format(BlockRandomisationViolation + "Block randomising information changed from {0}g {1} to {2}g {3}", 
                        participant.AdmissionWeight, 
                        GenderString(participant.IsMale),
                        admissionWeight,
                        GenderString(isMale))
                });
                if (isEnvelopeRandomising)
                {
                    participant.BlockNumber = null;
                    participant.BlockSize = 0;
                }
                else
                {
                    RandomisingEngine.ReasignBlockRandomisingData(participant, isMale, admissionWeight, this);
                }
                returnVar |= UpdateParticipantViolationType.BlockCriteriaChanged;
            }
            if (admissionWeight > RandomisingEngine.MaxBirthWeightGrams)
            {
                pv.Add(new ProtocolViolation
                {
                    Details = string.Format("Retrospectively inelligible participant!: Weight changed from {0} to {1}", participant.AdmissionWeight, admissionWeight)
                });

                participant.AdmissionWeight = admissionWeight;
                returnVar |= UpdateParticipantViolationType.IneligibleWeight;
            }
            if (participant.MultipleSiblingId != multipleSibblingId)
            {
                pv.Add(new ProtocolViolation
                {
                    Details = string.Format(BlockRandomisationViolation + "Twin/tripplet ID changed from '{0}' to '{1}';", participant.MultipleSiblingId, multipleSibblingId)
                });
                participant.MultipleSiblingId = multipleSibblingId;
                returnVar |= UpdateParticipantViolationType.MultipleSiblingIdChanged;
            }
            int nextPvId = GetNextId(_dbContext.ProtocolViolations, participant.CentreId);
            foreach (var p in pv)
            {
                p.ParticipantId = participant.Id;
                p.Id = nextPvId++;
                p.ViolationType = ViolationTypeOption.MajorWrongAllocation;
                p.ReportingTimeLocal = DateTime.Now;
                p.ReportingInvestigator = System.Threading.Thread.CurrentPrincipal.Identity.Name;
                _dbContext.ProtocolViolations.Add(p);
            }
            participant.Name = name;
            participant.PhoneNumber = phoneNumber;
            participant.AdmissionDiagnosis = AdmissionDiagnosis;
            participant.DateTimeBirth = dateTimeBirth;
            participant.GestAgeBirth = gestAgeBirth;
            participant.HospitalIdentifier = hospitalIdentifier;
            participant.Inborn = isInborn;
            participant.RegisteredAt = registeredAt;
            _dbContext.SaveChanges();
            if (this.ParticipantUpdated != null)
            {
                this.ParticipantUpdated(this, new ParticipantEventArgs(participant));
            }
            return returnVar;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="causeOfDeath"></param>
        /// <param name="otherCauseOfDeathDetail"></param>
        /// <param name="bcgAdverse"></param>
        /// <param name="bcgAdverseDetail"></param>
        /// <param name="bcgPapule"></param>
        /// <param name="lastContactWeight"></param>
        /// <param name="lastWeightDate"></param>
        /// <param name="dischargeDateTime"></param>
        /// <param name="deathOrLastContactDateTime"></param>
        /// <param name="outcomeAt28Days"></param>
        /// <param name="notes"></param>
        /// <param name="vaccinesAdministered">if null (or ommitted) the vaccines administered will not be altered</param>
        public void UpdateParticipant(int id,
            CauseOfDeathOption causeOfDeath,
            string otherCauseOfDeathDetail,
            bool? bcgAdverse,
            string bcgAdverseDetail,
            bool? bcgPapule,
            int? lastContactWeight,
            DateTime? lastWeightDate,
            DateTime? dischargeDateTime,
            DateTime? deathOrLastContactDateTime,
            OutcomeAt28DaysOption outcomeAt28Days,
            string notes,
            IEnumerable<VaccineAdministered> vaccinesAdministered=null)
        {
            Participant participant = _dbContext.Participants.Find(id);
            participant.CauseOfDeath = causeOfDeath;
            participant.OtherCauseOfDeathDetail = otherCauseOfDeathDetail;
            participant.BcgAdverse = bcgAdverse;
            participant.BcgAdverseDetail = bcgAdverseDetail;
            participant.BcgPapule = bcgPapule;
            participant.LastContactWeight = lastContactWeight;
            participant.LastWeightDate = lastWeightDate;
            participant.DischargeDateTime = dischargeDateTime;
            participant.DeathOrLastContactDateTime = deathOrLastContactDateTime;
            participant.OutcomeAt28Days = outcomeAt28Days;
            participant.RecordLastModified = DateTime.UtcNow;
            participant.Notes = notes;
            Participant attachedParticipant = _dbContext.Participants.Local.FirstOrDefault(p => p.Id == participant.Id);
            if (attachedParticipant == null)
            {
                _dbContext.Participants.Attach(participant);
            }
            else
            {
                _dbContext.Entry(attachedParticipant).CurrentValues.SetValues(participant);
            }
            _dbContext.Entry(participant).State = EntityState.Modified;
            if (vaccinesAdministered!=null)
            {
                var vas = VaccinesAdministered.ToList();
                AddOrUpdateVaccinesAdministered(participant.Id, vas);
                participant.VaccinesAdministered = vas;
            }
            _dbContext.SaveChanges();
            if (this.ParticipantUpdated != null)
            {
                this.ParticipantUpdated(this, new ParticipantEventArgs(participant));
            }
        }
        /// <summary>
        /// Adds or updates all vaccines attached to participant.VaccinesAdministered
        /// </summary>
        /// <param name="vaccinesAdministered"></param>
        public void AddOrUpdateVaccinesFor(int participantId, IEnumerable<VaccineAdministered> vaccinesAdministered)
        {
            AddOrUpdateVaccinesAdministered(participantId, vaccinesAdministered);
            _dbContext.SaveChanges();
            if (this.ParticipantUpdated != null)
            {
                var part = FindParticipant(participantId);
                part.VaccinesAdministered = new List<VaccineAdministered>(vaccinesAdministered);
                this.ParticipantUpdated(this, new ParticipantEventArgs(part));
            }
        }

        void AddOrUpdateVaccinesAdministered(int participantId, IEnumerable<VaccineAdministered> vaccinesAdministered)
        {
            var includedVaccineAdministeredIds = (from v in vaccinesAdministered
                                                  where v.Id != 0
                                                  select v.Id).ToArray();
            var removeVaccineAdministeredIds = (from v in _dbContext.VaccinesAdministered
                                                where v.ParticipantId == participantId && !includedVaccineAdministeredIds.Contains(v.Id)
                                                select v.Id).ToArray();
            if (removeVaccineAdministeredIds.Any())
            {
                string sqlString = string.Format("DELETE FROM {0} WHERE Id IN ({1})",
                    VaccineAdministered.VaccineAdminTableName,
                    string.Join(",", removeVaccineAdministeredIds));
                _dbContext.Database.ExecuteSqlCommand(sqlString);
            }
            int studyCentreId = (from p in _dbContext.Participants
                                 where p.Id == participantId
                                 select p.CentreId).First();
            int nextId = GetNextId(_dbContext.VaccinesAdministered, studyCentreId);
            foreach (var v in vaccinesAdministered)
            {
                v.ParticipantId = participantId;
                if (v.Id == 0)
                {
                    v.Id = nextId++;
                    _dbContext.VaccinesAdministered.Add(v);
                }
                else
                {
                    VaccineAdministered attachedVA = _dbContext.VaccinesAdministered.Local.FirstOrDefault(vax => vax.Id == v.Id);
                    if (attachedVA == null)
                    {
                        _dbContext.VaccinesAdministered.Attach(v);
                        _dbContext.Entry(v).State = EntityState.Modified;
                    }
                    else
                    {
                        _dbContext.Entry(attachedVA).CurrentValues.SetValues(v);
                    }
                }
            }
        }
        public void Add(Vaccine newVaccine)
        {
            newVaccine.Id = GetNextId(_dbContext.Vaccines, LocalStudyCentres.First().Id);
            _dbContext.Vaccines.Add(newVaccine);
            _dbContext.SaveChanges();
        }
        public void Update(Participant patient)
        {
            Update(new Participant[] { patient });
        }
        public void Update(IEnumerable<Participant> patients)
        {
            if (patients.Any(p=>p.Id==0))
            {
                throw new DataKeyOutOfRangeException("all patients for update must have an Id != 0");
            }
            var allIds = patients.Select(p=>p.Id);
            var allParticipants = (from p in _dbContext.Participants
                                   where allIds.Contains(p.Id)
                                   select p).ToList();
            foreach (Participant p in patients)
            {
                _dbContext.Entry(allParticipants.First(a=>a.Id == p.Id)).CurrentValues.SetValues(p);
            }
            _dbContext.SaveChanges();
        }
        public void Update(ScreenedPatient patient)
        {
            ScreenedPatient attachedPatient = _dbContext.ScreenedPatients.Local.FirstOrDefault(p => p.Id == patient.Id);
            if (attachedPatient == null)
            {
                _dbContext.ScreenedPatients.Attach(patient);
            }
            else
            {
                _dbContext.Entry(attachedPatient).CurrentValues.SetValues(patient);
            }
            _dbContext.Entry(patient).State = EntityState.Modified;
            _dbContext.SaveChanges();
        }
        public void Backup()
        {
            string bakFileName = _dbContext.BackupDb();
            FileInfo bakFile = new FileInfo(bakFileName);
            if (!bakFile.Exists) { return; }
#if DEBUG
            if (!(CloudDirectories.Count() == 1))
            {
                throw new InvalidOperationException("Backup called without cloud directories set to a single directory");
            }
#endif
            string cloudPathWithoutExtension = Path.GetFileNameWithoutExtension(bakFileName) + '_' + LocalStudyCentres.First().DuplicateIdCheck.ToString("N");
            string cloudZipName = CloudDirectories.First() + '\\' + cloudPathWithoutExtension + ".zip"; 
            
            var cloudFile = new FileInfo(cloudZipName);

            if (cloudFile.Exists)
            {
                DateTime? mostRecentEntry = (new DateTime?[]
                    {
                        _dbContext.Participants.Max(p=>(DateTime?)p.RecordLastModified),
                        _dbContext.ScreenedPatients.Max(s=>(DateTime?)s.RecordLastModified),
                        _dbContext.Vaccines.Max(v=>(DateTime?)v.RecordLastModified),
                        _dbContext.VaccinesAdministered.Max(va=>(DateTime?)va.RecordLastModified),
                        _dbContext.ProtocolViolations.Max(pv=>(DateTime?)pv.RecordLastModified),
                        _dbContext.StudyCentres.Max(s=>(DateTime?)s.RecordLastModified)
                    }).Max();
                if (mostRecentEntry==null || cloudFile.LastWriteTimeUtc >= mostRecentEntry)
                {
                    return;
                }
            }
            _dbContext.Dispose(); // only necessary for ce
            string cloudBakName = cloudPathWithoutExtension + Path.GetExtension(bakFileName);
            using (ZipFile zip = new ZipFile())
            {
                zip.AddFile(bakFile.FullName, "").FileName = cloudBakName;
                zip.Save(cloudFile.FullName);
            }
            _dbContext = _createContext.Invoke();
        }

        public void Restore()
        {
            //DateTime RecordLastModified = _dbContext.DbLastModifiedUtc(); //note this is all assuming user on backup end has no ability to modify data
            IEnumerable<MatchedFilePair> filePairs = GetMatchedCloudAndExtractedFiles()
                .Where(fp => fp.ExtractedBak == null || fp.ExtractedBak.CreationTimeUtc < fp.Zip.LastWriteTimeUtc)
                .ToList();
            
            if (!filePairs.Any()) { return; }
            List<BakFileDetails> bakDetails = new List<BakFileDetails>();
            _dbContext.Dispose();

            foreach (var fp in filePairs)
            {
                using (ZipFile readFile = ZipFile.Read(fp.Zip.FullName))
                {
                    readFile[0].Extract(App.DataDirectory, ExtractExistingFileAction.OverwriteSilently);
                }
                BakFileDetails newBak = new BakFileDetails();
                bakDetails.Add(newBak);
                if (fp.ExtractedBak == null)
                {
                    fp.ExtractedBak = new FileInfo(Path.Combine(App.DataDirectory, Path.GetFileNameWithoutExtension(fp.Zip.Name) + BakExtension));
                }
                else
                {
                    newBak.LastBackupUtc = fp.ExtractedBak.CreationTimeUtc;
                }
                newBak.FullFilename = fp.ExtractedBak.FullName;
                fp.ExtractedBak.CreationTimeUtc = fp.Zip.LastWriteTimeUtc;
            }

            _dbContext = _createContext.Invoke();
            AddOrUpdateBaks(bakDetails);

        }
        public IEnumerable<KeyValuePair<string,IEnumerable<StudyCentreModel>>> GetFilenamesAndCentres()
        {
            Restore();
            var returnVar = new List<KeyValuePair<string,IEnumerable<StudyCentreModel>>>();
            foreach (var p in GetMatchedCloudAndExtractedFiles())
            {
                if (p.ExtractedBak!=null)
                {
                    MigrateIfRequired(p.ExtractedBak.FullName);
                    using (var db = _dbContext.AttachDb(p.ExtractedBak.FullName))
                    {
                        returnVar.Add(new KeyValuePair<string,IEnumerable<StudyCentreModel>>(p.Zip.FullName,db.StudyCentres.Select(s=>s.Id).ToList().Select(s=>LocalStudyCentreDictionary[s]).ToArray()));
                    }
                }
            }
            return returnVar;
        }

        public string BackupLimitedDbTo(string directory, params StudyCentreModel[] studyCentres)
        {
            const string bakExtension = ".sdf";
            //this is a hack, and will not work if moving to a full sql server instance
            Restore();
            string destination = Path.Combine(directory, _dbContext.DbName + '_' + studyCentres.First().DuplicateIdCheck.ToString("N")) + bakExtension;
            File.Copy(App.DataDirectory + '\\' + _dbContext.DbName + bakExtension, destination, true);
            string whereString = "WHERE NOT (" + studyCentres.Select(s => string.Format("(Id BETWEEN '{0}' AND '{1}')", s.Id, s.MaxIdForSite)).Aggregate((c, n) => c + " OR " + n) + ')';
            using (var db = _dbContext.AttachDb(destination))
            {
                foreach (string table in new string[] { "StudyCentres", "Participants", "ProtocolViolations", "ScreenedPatients", "VaccinesAdministered" })
                {
                    db.Database.ExecuteSqlCommand("Delete from [" + table + "] " + whereString);
                }
            }
            return destination;
        }
        void MigrateIfRequired(string fullFilename)
        {
            if (_baksForgoingMigration == null) { _baksForgoingMigration = new List<string>(); }
            if (!_baksForgoingMigration.Contains(fullFilename))
            {
                if (!CodeBasedMigration.ApplyPendingMigrations<BlowTrial.Migrations.TrialData.TrialDataConfiguration>(TrialDataContext.GetConnectionString(fullFilename), ContextCeConfiguration.ProviderInvariantName))
                {
                    _baksForgoingMigration.Add(fullFilename);
                }
            }
        }
        void AddOrUpdateBaks(IEnumerable<BakFileDetails> bakupFiles)
        {
            List<StudyCentre> knownSites = _dbContext.StudyCentres.ToList();
            List<IntegerRange> newSiteIdRanges = new List<IntegerRange>();
            List<Participant> updatedParticipants = null;
            List<Participant> addedParticipants = null;
            List<ScreenedPatient> addedScreenPatients = null;
            foreach (var f in bakupFiles)
            {
                MigrateIfRequired(f.FullFilename);
                IEnumerable<Guid> knownSiteIds = knownSites.Select(s => s.DuplicateIdCheck);
                using (var downloadedDb = _dbContext.AttachDb(f.FullFilename))
                {      
                    var newSites = (from s in downloadedDb.StudyCentres.AsNoTracking()
                                    where !knownSiteIds.Contains(s.DuplicateIdCheck)
                                    select s).ToList();
                    foreach (StudyCentre s in newSites)
                    {
                        var dup = knownSites.FirstOrDefault(k=>k.Id == s.Id);
                        if (dup != null)
                        {
                            throw new DuplicateDataKeyException(string.Format("Duplicate key for site Id:{0} (using Guids {1} & {2})",s.Id.ToString(),s.DuplicateIdCheck, dup.DuplicateIdCheck));
                        }
                        var overlappingSite = knownSites.FirstOrDefault(k=>s.Id <= k.MaxIdForSite && s.MaxIdForSite >= k.Id);
                        if (overlappingSite != null)
                        {
                            throw new OverlappingDataKeyRangeException(string.Format("Potential for patient data overwrite - existing site Ids ({0}-{1}), new site Ids ({2}-{3})",
                                overlappingSite.Id, overlappingSite.MaxIdForSite,
                                s.Id, s.MaxIdForSite));
                        }
                        _dbContext.StudyCentres.Add(s); //because context will have been renewed, this should not cause a duplicate key exception
                        knownSites.Add(s);
                        newSiteIdRanges.Add(new IntegerRange(s.Id, s.MaxIdForSite));
                        _localStudyCentreDictionary = null;
                    }
                    var newSiteIds = newSiteIdRanges.Select(n => n.Min);
                    DateTime minDate = System.Data.SqlTypes.SqlDateTime.MinValue.Value;
                    DateTime mostRecentBak = (f.LastBackupUtc < minDate)?minDate:f.LastBackupUtc;

                    var vaccinePredicate = PredicateBuilder.False<Vaccine>();
                    vaccinePredicate.Or(p => p.RecordLastModified > mostRecentBak);
                    foreach (IntegerRange rng in newSiteIdRanges)
                    {
                        vaccinePredicate = vaccinePredicate.Or(p => p.Id >= rng.Min && p.Id <= rng.Max);
                    }
                    var newVax = _dbContext.Vaccines.AsNoTracking().AsExpandable().Where(vaccinePredicate).ToArray();
                    _dbContext.Vaccines.AddOrUpdate(newVax);

                    var vaccineAdminPredicate = PredicateBuilder.False<VaccineAdministered>();
                    vaccineAdminPredicate.Or(p => p.RecordLastModified > mostRecentBak);
                    foreach (IntegerRange rng in newSiteIdRanges)
                    {
                        vaccineAdminPredicate = vaccineAdminPredicate.Or(p => p.Id >= rng.Min && p.Id <= rng.Max);
                    }
                    var newVaxAdmin = _dbContext.VaccinesAdministered.AsNoTracking().AsExpandable().Where(vaccineAdminPredicate).ToArray();
                    _dbContext.VaccinesAdministered.AddOrUpdate(newVaxAdmin);

                    var newOrUpdatedParticipants = (from p in downloadedDb.Participants.AsNoTracking()
                                        where p.RecordLastModified > mostRecentBak || newSiteIds.Contains(p.CentreId)
                                        select p).ToArray();
                    //move this down - need to check if vaccines administered
                    bool isPartAddedEvt = this.ParticipantAdded != null;
                    bool isPartUpdEvt = this.ParticipantUpdated != null;
                    ILookup<bool, Participant> partAlreadyInDb;
                    if (isPartAddedEvt || isPartUpdEvt)
                    {
                        int[] currentPartIds = _dbContext.Participants.Select(p => p.Id).ToArray();
                        partAlreadyInDb = newOrUpdatedParticipants.ToLookup(p => currentPartIds.Contains(p.Id));
                    }
                    else { partAlreadyInDb = null; } //purely to avoid use of unassigned variable error 
                    _dbContext.Participants.AddOrUpdate(newOrUpdatedParticipants);
                    if (isPartUpdEvt)
                    {
                        int[] newVaxAdminPartId = newVaxAdmin.Select(va => va.ParticipantId).Distinct().Except(newOrUpdatedParticipants.Select(p => p.Id)).ToArray();
                        if (updatedParticipants==null) 
                        {
                            updatedParticipants = new List<Participant>();
                        }
                        updatedParticipants.AddRange((from Participant p in _dbContext.Participants.AsNoTracking()
                                                      where newVaxAdminPartId.Contains(p.Id)
                                                      select p).AsEnumerable());
                        updatedParticipants.AddRange(partAlreadyInDb[true]);
                    }
                    if (isPartAddedEvt)
                    {
                        if (addedParticipants == null)
                        {
                            addedParticipants = new List<Participant>();
                        }
                        addedParticipants.AddRange(partAlreadyInDb[false]);
                    }

                    var screenedPatients = (from s in downloadedDb.ScreenedPatients.AsNoTracking()
                                            where s.RecordLastModified > mostRecentBak || newSiteIds.Contains(s.CentreId)
                                            select s).ToArray();
                    _dbContext.ScreenedPatients.AddOrUpdate(screenedPatients);
                    if (this.ScreenedPatientAdded != null)
                    {
                        if (addedScreenPatients == null)
                        {
                            addedScreenPatients = new List<ScreenedPatient>();
                        }
                        int[] currentScreenedPatients = _dbContext.ScreenedPatients.Select(s => s.Id).ToArray();
                        addedScreenPatients.AddRange(ScreenedPatients.Where(s => !currentScreenedPatients.Contains(s.Id)).AsEnumerable());
                    
                    }

                    var protocolViolPredicate = PredicateBuilder.False<ProtocolViolation>();
                    protocolViolPredicate.Or(p => p.RecordLastModified > mostRecentBak);
                    foreach (IntegerRange rng in newSiteIdRanges)
                    {
                        protocolViolPredicate = protocolViolPredicate.Or(p => p.Id >= rng.Min && p.Id <= rng.Max);
                    }
                    var newProtocolViol = _dbContext.ProtocolViolations.AsNoTracking().AsExpandable().Where(protocolViolPredicate).ToArray();
                    _dbContext.ProtocolViolations.AddOrUpdate(newProtocolViol);
                    if (ProtocolViolationAdded != null)
                    {
                        foreach(ProtocolViolation pv in newProtocolViol)
                        {
                            ProtocolViolationAdded(this,new ProtocolViolationEventArgs(pv));
                        }
                    }

                    _dbContext.SaveChanges();
                }
            }
            if (addedParticipants != null)
            {
                foreach(var p in addedParticipants)
                {
                    p.VaccinesAdministered = _dbContext.VaccinesAdministered.Where(va => va.ParticipantId == p.Id).ToList();
                    ParticipantAdded(this, new ParticipantEventArgs(p));
                }
            }
            if (updatedParticipants != null)
            {
                foreach (var p in updatedParticipants)
                {
                    p.VaccinesAdministered = _dbContext.VaccinesAdministered.Where(va => va.ParticipantId == p.Id).ToList();
                    ParticipantUpdated(this, new ParticipantEventArgs(p));
                }
            }
            if (addedScreenPatients != null)
            {
                foreach (var s in addedScreenPatients)
                {
                    ScreenedPatientAdded(this, new ScreenedPatientEventArgs(s));
                }
            }
        }
        public ParticipantsSummary GetParticipantSummary()
        {
            return new ParticipantsSummary
            {
                TotalCount = _dbContext.Participants.Count(),
                InterventionArmCount = _dbContext.Participants.Count(p => p.IsInterventionArm),
                CompletedRecordCount = _dbContext.Participants.Select(ParticipantBaseModel.GetDataRequiredExpression()).Count(d => d == DataRequiredOption.Complete)
            };
        }
        public ScreenedPatientsSummary GetScreenedPatientSummary()
        {
            return new ScreenedPatientsSummary
            {
                TotalCount = _dbContext.ScreenedPatients.Count(),
                BadInfectnImmuneCount = _dbContext.ScreenedPatients.Count(s => s.BadInfectnImmune),
                BadMalformCount = _dbContext.ScreenedPatients.Count(s => s.BadMalform),
                LikelyDie24HrCount = _dbContext.ScreenedPatients.Count(s => s.LikelyDie24Hr),
                MissedCount = _dbContext.ScreenedPatients.Count(s => s.Missed==true),
                RefusedConsentCount = _dbContext.ScreenedPatients.Count(s => s.RefusedConsent==true),
                WasGivenBcgPriorCount = _dbContext.ScreenedPatients.Count(s => s.WasGivenBcgPrior)
            };
        }

        int GetNextId(IQueryable<ISharedRecord> recordSet, int studyCentreId)
        {
            int maxIdForSite = LocalStudyCentres.First(s=>s.Id == studyCentreId).MaxIdForSite;
            int returnVar = (from r in recordSet
                             where r.Id >= studyCentreId && r.Id <= maxIdForSite
                             select r.Id).DefaultIfEmpty().Max();
            if (returnVar == 0) { returnVar = studyCentreId; }
            returnVar++; 
            if (returnVar > maxIdForSite)
            {
                throw new DataKeyOutOfRangeException("Database has exceeded maximum size for site");
            }
            return returnVar;
        }

        ICollection<MatchedFilePair> GetMatchedCloudAndExtractedFiles()
        {
            string filePrefix = _dbContext.DbName;
            int prefixLen = filePrefix.Length;
            int fnLen = prefixLen + 37;

            List<MatchedFilePair> returnVar = new List<MatchedFilePair>();

            DirectoryInfo di;
            foreach (string dirName in CloudDirectories)
            {
                di = new DirectoryInfo(dirName);
                returnVar.AddRange(from f in di.GetFiles()
                                   where f.Name.Length == fnLen && f.Name.Substring(0, prefixLen) == filePrefix && f.Extension==".zip"
                                   select new MatchedFilePair{ Zip = f });
            }

            di = new DirectoryInfo(App.DataDirectory);
            foreach (FileInfo f in di.GetFiles())
            {
                if (f.Name.Length == fnLen && f.Name.Substring(0, prefixLen) == filePrefix && f.Extension == BakExtension)
                {
                    var matchedPair = returnVar.FirstOrDefault(r => Path.GetFileNameWithoutExtension(r.Zip.Name) == Path.GetFileNameWithoutExtension(f.Name));
                    if (matchedPair != null)
                    {
                        matchedPair.ExtractedBak = f;
                    }
                }
            }

            return returnVar;
        }

        #endregion // Methods

        #region IDisposable Implementation
        //http://msdn.microsoft.com/en-us/library/vstudio/b1yfkh5e%28v=vs.100%29.aspx
        private bool _disposed = false;
        public void Dispose()
        {
            Dispose(true);

            // Use SupressFinalize in case a subclass 
            // of this type implements a finalizer.
            GC.SuppressFinalize(this);
        }
        ~Repository()
        {
            Dispose(false);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_dbContext != null) { _dbContext.Dispose(); }
                }

                // Indicate that the instance has been disposed.
                _disposed = true;
            }
        }
        #endregion // IDiposable

        #region MatchedFilePair
        private class MatchedFilePair
        {
            FileInfo _zip;
            FileInfo _extractedBak;
            internal FileInfo Zip 
            {
                get { return _zip; }
                set 
                { 
                    if (value.Extension!=".zip")
                    {
                        throw new InvalidFileTypeException(value.Extension,".zip");
                    }
                    _zip = value;
                }
            }
            internal FileInfo ExtractedBak 
            {
                get { return _extractedBak; }
                set
                {
                    if (value.Extension!=BakExtension)
                    {
                        throw new InvalidFileTypeException(value.Extension, BakExtension);
                    }
                    _extractedBak = value;
                }
            }
        }
        #endregion

        #region BakFileDetails
        private class BakFileDetails
        {
            internal string FullFilename { get; set; }
            internal DateTime LastBackupUtc { get; set; }
        }
        #endregion
    }
}
